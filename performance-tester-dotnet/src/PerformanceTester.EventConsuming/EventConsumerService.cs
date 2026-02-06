using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Threading.Channels;

namespace PerformanceTester.EventConsuming;

/// <summary>
/// BackgroundService that consumes CloudEvents from RabbitMQ and tracks throughput.
/// Implements IEventConsumer interface for orchestrator control.
/// Writes EventThroughputSample data to Channel for MetricsCollectorService.
/// </summary>
internal sealed class EventConsumerService : BackgroundService, IEventConsumer
{
    private const string ExchangeName = "referenceservice.comparison";
    private const string RoutingKey = "instrumentstatus.kpi.updated";
    private const int PrefetchCount = 50;

    private readonly string _connectionString;
    private readonly string _queueName;
    private readonly ILogger<EventConsumerService> _logger;
    private readonly ThroughputTracker _throughputTracker;
    private readonly Channel<EventThroughputSample> _throughputChannel;

    // Event tracking state
    private int _receivedEventCount;
    private TaskCompletionSource<bool>? _trackingCompletionSource;
    private int _expectedCount;
    private Timer? _inactivityTimer;
    private DateTime _lastEventReceivedTime;
    private TimeSpan _inactivityTimeout;
    private CancellationToken _trackingCancellationToken;

    // Progress reporter for StartTrackingEventsAsync (stored for OnMessageReceivedAsync use)
    private IProgress<ConsumerPhaseInfo>? _progress;

    // RabbitMQ connection state
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _isConnected;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public EventConsumerService(
        string connectionString,
        string queueName,
        Channel<EventThroughputSample> throughputChannel,
        ILogger<EventConsumerService> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
        _throughputChannel = throughputChannel ?? throw new ArgumentNullException(nameof(throughputChannel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _throughputTracker = new ThroughputTracker();
    }

    /// <inheritdoc />
    public async Task ConnectAsync(
        IProgress<ConsumerPhaseInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_isConnected)
            {
                throw new InvalidOperationException("Already connected to RabbitMQ");
            }

            _logger.LogInformation("EventConsumer connecting to RabbitMQ...");
            progress?.Report(ConsumerPhaseInfo.Starting(ConsumerPhase.Connecting, "Connecting to RabbitMQ"));

            // Create connection
            var factory = new ConnectionFactory { Uri = new Uri(_connectionString) };
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

            _logger.LogInformation("✓ Connected to RabbitMQ");

            // Declare exchange
            await _channel.ExchangeDeclareAsync(
                exchange: ExchangeName,
                type: "topic",
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            // Declare queue
            await _channel.QueueDeclareAsync(
                queue: _queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            // Bind queue to exchange
            await _channel.QueueBindAsync(
                queue: _queueName,
                exchange: ExchangeName,
                routingKey: RoutingKey,
                arguments: null,
                cancellationToken: cancellationToken);

            _logger.LogInformation("✓ Queue '{QueueName}' bound to exchange '{ExchangeName}' with routing key '{RoutingKey}'",
                _queueName,
                ExchangeName,
                RoutingKey);

            // Set prefetch count
            await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: PrefetchCount, global: false, cancellationToken: cancellationToken);

            // Create async consumer
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += OnMessageReceivedAsync;

            // Start consuming
            await _channel.BasicConsumeAsync(
                queue: _queueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: cancellationToken);

            _logger.LogInformation("✓ Consumer started, listening for events...");
            progress?.Report(ConsumerPhaseInfo.Completed(ConsumerPhase.ConsumerRegistered,
                $"Consumer registered on queue '{_queueName}'"));

            _isConnected = true;
            progress?.Report(ConsumerPhaseInfo.Completed(ConsumerPhase.Connected, "Connected to RabbitMQ"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to connect to RabbitMQ");
            progress?.Report(ConsumerPhaseInfo.Failed(ConsumerPhase.Connecting, ex.Message));

            // Cleanup on failure
            if (_channel != null)
            {
                await _channel.CloseAsync();
                _channel.Dispose();
                _channel = null;
            }
            if (_connection != null)
            {
                await _connection.CloseAsync();
                _connection.Dispose();
                _connection = null;
            }

            throw new InvalidOperationException("Failed to connect to RabbitMQ", ex);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(
        IProgress<ConsumerPhaseInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (!_isConnected)
            {
                _logger.LogDebug("Not connected, disconnect is no-op");
                return;
            }

            _logger.LogInformation("EventConsumer disconnecting from RabbitMQ...");
            progress?.Report(ConsumerPhaseInfo.Starting(ConsumerPhase.Disconnecting, "Disconnecting from RabbitMQ"));

            // Generate final throughput sample to capture any remaining events
            var finalSample = _throughputTracker.GetFinalSample();
            if (finalSample != null)
            {
                await _throughputChannel.Writer.WriteAsync(finalSample);
                _logger.LogDebug("Final throughput sample written: {EventsPerSec:F1} events/sec, {Total} total",
                    finalSample.ThroughputEventsPerSecond,
                    finalSample.CumulativeEventCount);
            }

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
                _channel = null;
            }
            if (_connection != null)
            {
                await _connection.CloseAsync();
                _connection.Dispose();
                _connection = null;
            }

            _isConnected = false;
            _logger.LogInformation("✓ EventConsumer disconnected");
            progress?.Report(ConsumerPhaseInfo.Completed(ConsumerPhase.Disconnecting, "Disconnected from RabbitMQ"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during disconnect");
            progress?.Report(ConsumerPhaseInfo.Failed(ConsumerPhase.Disconnecting, ex.Message));
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <inheritdoc />
    public Task StartTrackingEventsAsync(
        int expectedCount,
        TimeSpan inactivityTimeout,
        IProgress<ConsumerPhaseInfo>? progress = null,
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

        // Store progress reporter for OnMessageReceivedAsync to use
        _progress = progress;

        // Report tracking started
        progress?.Report(ConsumerPhaseInfo.Starting(ConsumerPhase.TrackingStarted,
            $"Tracking {expectedCount} events with {inactivityTimeout.TotalSeconds}s inactivity timeout"));

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

        // Check for cancellation first
        if (_trackingCancellationToken.IsCancellationRequested)
        {
            var currentCount = Interlocked.CompareExchange(ref _receivedEventCount, 0, 0);
            _logger.LogWarning(
                "Event tracking cancelled (received {Current}/{Expected})",
                currentCount,
                _expectedCount);

            _trackingCompletionSource.TrySetCanceled(_trackingCancellationToken);
            _inactivityTimer?.Dispose();
            _inactivityTimer = null;
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

            // Connect if not already connected (supports both explicit ConnectAsync and BackgroundService patterns)
            // NOTE: When called via BackgroundService, no progress parameter is passed
            if (!_isConnected)
            {
                await ConnectAsync(progress: null, cancellationToken: stoppingToken);
            }

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
            // Disconnect and cleanup (no progress passed from BackgroundService)
            await DisconnectAsync(progress: null);
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
        _progress = null;
        _lastEventReceivedTime = DateTime.UtcNow;
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs eventArgs)
    {
        // Capture channel reference to avoid TOCTOU race with DisconnectAsync
        // If channel is null (shutting down), we skip ACK/NACK gracefully
        var channel = _channel;

        try
        {
            // Increment received count (thread-safe)
            var count = Interlocked.Increment(ref _receivedEventCount);

            // Reset inactivity timer
            _lastEventReceivedTime = DateTime.UtcNow;

            // Note: We don't validate CloudEvents structure here because different services
            // (Java, Go, Rust, etc.) may use slightly different CloudEvents formats.
            // The consumer's job is just to count received messages, not validate their structure.
            // The message body is available in eventArgs.Body if validation is needed in the future.

            // Record throughput sample
            var sample = _throughputTracker.RecordEvent();
            if (sample != null)
            {
                // Write sample to channel (non-blocking)
                await _throughputChannel.Writer.WriteAsync(sample, _trackingCancellationToken);
            }

            // Report event received (every event so tests can wait for specific counts)
            _progress?.Report(ConsumerPhaseInfo.Completed(ConsumerPhase.EventReceived,
                eventCount: count,
                message: $"Received event {count}/{_expectedCount}"));

            // Log progress every 1000 events
            if (count % 1000 == 0)
            {
                _logger.LogDebug("Consumed {Current} events...", count);
            }

            // Check if target reached
            if (_trackingCompletionSource != null && count >= _expectedCount)
            {
                _logger.LogInformation("✓ Target count reached: {Current}/{Expected} events", count, _expectedCount);

                // Report target reached
                _progress?.Report(ConsumerPhaseInfo.Completed(ConsumerPhase.TargetReached,
                    eventCount: count,
                    message: $"Target reached: {count} events"));

                // Write final throughput sample to capture any remaining events not yet sampled
                // (e.g., if all events processed in < 500ms sampling interval)
                var finalSample = _throughputTracker.GetFinalSample();
                if (finalSample != null)
                {
                    await _throughputChannel.Writer.WriteAsync(finalSample, _trackingCancellationToken);
                    _logger.LogDebug("Final throughput sample at target: {EventsPerSec:F1} events/sec, {Total} total",
                        finalSample.ThroughputEventsPerSecond,
                        finalSample.CumulativeEventCount);
                }

                _trackingCompletionSource.TrySetResult(true);
                _inactivityTimer?.Dispose();
                _inactivityTimer = null;
            }

            // Manual ACK after processing (using captured reference)
            if (channel != null)
            {
                await channel.BasicAckAsync(deliveryTag: eventArgs.DeliveryTag, multiple: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing message");

            // NACK message and requeue (using captured reference)
            if (channel != null)
            {
                try
                {
                    await channel.BasicNackAsync(
                        deliveryTag: eventArgs.DeliveryTag,
                        multiple: false,
                        requeue: true);
                }
                catch (Exception nackEx)
                {
                    // Channel may have been disposed during shutdown — log and continue
                    _logger.LogDebug(nackEx, "Failed to NACK message (channel may be closing)");
                }
            }
        }
    }
}
