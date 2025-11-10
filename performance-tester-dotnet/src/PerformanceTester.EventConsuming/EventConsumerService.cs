using CloudNative.CloudEvents;
using CloudNative.CloudEvents.SystemTextJson;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Threading.Channels;

namespace PerformanceTester.EventConsuming;

/// <summary>
/// BackgroundService that consumes CloudEvents from RabbitMQ and tracks throughput.
/// Implements IEventConsumer interface for orchestrator control.
/// Writes ThroughputSample data to Channel for MetricsCollectorService.
/// </summary>
internal sealed class EventConsumerService : BackgroundService, IEventConsumer
{
    private const string ExchangeName = "referenceservice.comparison";
    private const string RoutingKey = "instrument.status.changed";
    private const int PrefetchCount = 50;

    private readonly string _connectionString;
    private readonly string _queueName;
    private readonly ILogger<EventConsumerService> _logger;
    private readonly CloudEventFormatter _formatter;
    private readonly ThroughputTracker _throughputTracker;
    private readonly Channel<ThroughputSample> _throughputChannel;

    // Event tracking state
    private int _receivedEventCount;
    private TaskCompletionSource<bool>? _trackingCompletionSource;
    private int _expectedCount;
    private Timer? _inactivityTimer;
    private DateTime _lastEventReceivedTime;
    private TimeSpan _inactivityTimeout;
    private CancellationToken _trackingCancellationToken;

    // RabbitMQ connection state
    private IConnection? _connection;
    private IChannel? _channel;

    public EventConsumerService(
        string connectionString,
        string queueName,
        Channel<ThroughputSample> throughputChannel,
        ILogger<EventConsumerService> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
        _throughputChannel = throughputChannel ?? throw new ArgumentNullException(nameof(throughputChannel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _formatter = new JsonEventFormatter();
        _throughputTracker = new ThroughputTracker();
    }

    /// <inheritdoc />
    public Task StartTrackingEventsAsync(
        int expectedCount,
        TimeSpan inactivityTimeout,
        CancellationToken cancellationToken = default)
    {
        if (expectedCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedCount), expectedCount, "Expected count must be at least 1");
        }

        if (inactivityTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(inactivityTimeout), inactivityTimeout, "Inactivity timeout cannot be negative");
        }

        _logger.LogInformation("=== Starting event tracking: expecting {Count} events, inactivity timeout: {Timeout}s ===",
            expectedCount,
            inactivityTimeout.TotalSeconds);

        // Reset for new tracking session (critical for test isolation)
        ResetTrackingState();

        _expectedCount = expectedCount;
        _inactivityTimeout = inactivityTimeout;
        _trackingCancellationToken = cancellationToken;
        _trackingCompletionSource = new TaskCompletionSource<bool>();
        _lastEventReceivedTime = DateTime.UtcNow;        

        // Start inactivity timer (checks every 1 second)
        _inactivityTimer = new Timer(
            callback: CheckInactivityTimeout,
            state: null,
            dueTime: TimeSpan.FromSeconds(1),
            period: TimeSpan.FromSeconds(1));

        return _trackingCompletionSource.Task;
    }

    private void CheckInactivityTimeout(object? state)
    {
        if (_trackingCompletionSource == null || _trackingCompletionSource.Task.IsCompleted)
        {
            return;
        }

        var timeSinceLastEvent = DateTime.UtcNow - _lastEventReceivedTime;
        if (timeSinceLastEvent > _inactivityTimeout)
        {
            var currentCount = Interlocked.CompareExchange(ref _receivedEventCount, 0, 0);
            _logger.LogError(
                "❌ Inactivity timeout expired: No events received for {Timeout}s (received {Current}/{Expected})",
                _inactivityTimeout.TotalSeconds,
                currentCount,
                _expectedCount);

            _trackingCompletionSource?.TrySetException(
                new TimeoutException(
                    $"Inactivity timeout expired: No events received for {_inactivityTimeout.TotalSeconds}s " +
                    $"(received {currentCount}/{_expectedCount})"));

            _inactivityTimer?.Dispose();
            _inactivityTimer = null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("EventConsumer starting...");

            // Create connection
            var factory = new ConnectionFactory { Uri = new Uri(_connectionString) };
            _connection = await factory.CreateConnectionAsync(stoppingToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            _logger.LogInformation("✓ Connected to RabbitMQ");

            // Declare exchange
            await _channel.ExchangeDeclareAsync(
                exchange: ExchangeName,
                type: "topic",
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken);

            // Declare queue
            await _channel.QueueDeclareAsync(
                queue: _queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken);

            // Bind queue to exchange
            await _channel.QueueBindAsync(
                queue: _queueName,
                exchange: ExchangeName,
                routingKey: RoutingKey,
                arguments: null,
                cancellationToken: stoppingToken);

            _logger.LogInformation("✓ Queue '{QueueName}' bound to exchange '{ExchangeName}' with routing key '{RoutingKey}'",
                _queueName,
                ExchangeName,
                RoutingKey);

            // Set prefetch count
            await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: PrefetchCount, global: false, cancellationToken: stoppingToken);

            // Create async consumer
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += OnMessageReceivedAsync;

            // Start consuming
            await _channel.BasicConsumeAsync(
                queue: _queueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken);

            _logger.LogInformation("✓ Consumer started, listening for events...");

            // Keep running until cancellation
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("EventConsumer stopping gracefully...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ EventConsumer failed");
            throw;
        }
        finally
        {
            // Complete throughput channel to signal MetricsCollectorService
            _throughputChannel.Writer.Complete();
            _logger.LogInformation("✓ Throughput channel completed");

            // Cleanup tracking state
            ResetTrackingState();

            // Cleanup resources
            if (_channel != null)
            {
                await _channel.CloseAsync();
                _channel.Dispose();
            }
            if (_connection != null)
            {
                await _connection.CloseAsync();
                _connection.Dispose();
            }

            _logger.LogInformation("✓ EventConsumer stopped");
        }
    }

    /// <summary>
    /// Resets tracking state for clean shutdown or between tracking sessions.
    /// </summary>
    private void ResetTrackingState()
    {
        _inactivityTimer?.Dispose();
        _inactivityTimer = null;
        Interlocked.Exchange(ref _receivedEventCount, 0);
        _trackingCompletionSource = null;
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs eventArgs)
    {
        try
        {
            // Increment received count (thread-safe)
            var count = Interlocked.Increment(ref _receivedEventCount);

            // Reset inactivity timer
            _lastEventReceivedTime = DateTime.UtcNow;

            // Deserialize CloudEvent (for validation)
            using var stream = new MemoryStream(eventArgs.Body.ToArray());
            var cloudEvent = await _formatter.DecodeStructuredModeMessageAsync(
                stream,
                new System.Net.Mime.ContentType("application/cloudevents+json"),
                null);

            // Record throughput sample
            var sample = _throughputTracker.RecordEvent();
            if (sample != null)
            {
                // Write sample to channel (non-blocking)
                await _throughputChannel.Writer.WriteAsync(sample, _trackingCancellationToken);
            }

            // Log progress every 1000 events
            if (count % 1000 == 0)
            {
                _logger.LogDebug("Consumed {Current} events...", count);
            }

            // Check if target reached
            if (_trackingCompletionSource != null && count >= _expectedCount)
            {
                _logger.LogInformation("✓ Target count reached: {Current}/{Expected} events", count, _expectedCount);
                _trackingCompletionSource.TrySetResult(true);
                _inactivityTimer?.Dispose();
                _inactivityTimer = null;
            }

            // Manual ACK after processing
            if (_channel != null)
            {
                await _channel.BasicAckAsync(deliveryTag: eventArgs.DeliveryTag, multiple: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing message");

            // NACK message and requeue
            if (_channel != null)
            {
                await _channel.BasicNackAsync(
                    deliveryTag: eventArgs.DeliveryTag,
                    multiple: false,
                    requeue: true);
            }
        }
    }
}
