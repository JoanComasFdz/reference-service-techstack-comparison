using CloudNative.CloudEvents;
using CloudNative.CloudEvents.SystemTextJson;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Net;
using Xunit.Abstractions;

namespace PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

/// <summary>
/// Configurable event publisher for integration tests.
/// Acts as a bi-directional mock service: consumes input events and publishes output events at a controlled rate.
/// Used for testing timeout scenarios and slow service behavior.
/// Exposes HTTP endpoints: /health (readiness), /kpi (mock data per reference service spec), / (service name).
/// </summary>
/// <param name="connectionString">RabbitMQ connection string</param>
/// <param name="output">Test output helper for logging</param>
/// <param name="exchangeName">RabbitMQ exchange name (default: matches production)</param>
/// <param name="inputRoutingKey">Input event routing key (default: matches EventPublishing)</param>
/// <param name="outputRoutingKey">Output event routing key (default: matches EventConsuming)</param>
/// <param name="inputQueueName">Queue name for consuming input events</param>
public sealed class ConfigurableReferenceService(
    string connectionString,
    ITestOutputHelper? output,
    string exchangeName = "referenceservice.comparison",
    string inputRoutingKey = "instrument.status.changed",
    string outputRoutingKey = "instrumentstatus.kpi.updated",
    string inputQueueName = "configurablereferenceservice-input") : IAsyncDisposable
{
    /// <summary>
    /// Default queue name for ConfigurableReferenceService input events.
    /// Tests should purge this queue at the end to prevent stale event accumulation.
    /// </summary>
    public const string DefaultInputQueueName = "configurablereferenceservice-input";
    private readonly string _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    private readonly ITestOutputHelper? _output = output;
    private readonly string _exchangeName = exchangeName ?? throw new ArgumentNullException(nameof(exchangeName));
    private readonly string _inputRoutingKey = inputRoutingKey ?? throw new ArgumentNullException(nameof(inputRoutingKey));
    private readonly string _outputRoutingKey = outputRoutingKey ?? throw new ArgumentNullException(nameof(outputRoutingKey));
    private readonly string _inputQueueName = inputQueueName ?? throw new ArgumentNullException(nameof(inputQueueName));
    private readonly CloudEventFormatter _formatter = new JsonEventFormatter();

    // Configuration
    private int _configuredEventCount = 1;
    private TimeSpan _delayBetweenEvents = TimeSpan.Zero;
    private int _warmupEventCount = 0;

    // Publish mode configuration
    private enum PublishMode { Normal, WarmupOnly, NoResponse }
    private PublishMode _publishMode = PublishMode.Normal;

    // Termination configuration (simulating process death)
    private int _terminateAfterEvents = int.MaxValue;
    private bool _terminationEnabled = false;

    // HTTP response configuration
    private int _httpStatusCode = 200;
    private int _failEveryNthRequest = 0;
    private int _kpiRequestCount = 0;

    // Connection state
    private IConnection? _connection;
    private IChannel? _inputChannel;
    private IChannel? _outputChannel;
    private bool _isConnected;

    // HTTP listener for service discovery
    private HttpListener? _httpListener;
    private Task? _httpListenerTask;

    // Tracking state
    private int _receivedInputEventCount;
    private TaskCompletionSource<bool>? _triggerCompletionSource;
    private TaskCompletionSource<bool>? _publicationCompletionSource;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    /// <summary>
    /// Configures the publication behavior (called before connecting).
    /// </summary>
    /// <param name="eventCount">Number of KPI events to publish after trigger</param>
    /// <param name="delayBetweenEvents">Delay between events (default: 0 = fast)</param>
    /// <param name="warmupEventCount">Number of warmup events to ignore before triggering (default: 0)</param>
    public void ConfigurePublication(int eventCount, TimeSpan? delayBetweenEvents = null, int warmupEventCount = 0)
    {
        if (eventCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(eventCount), eventCount, "Event count must be at least 1");
        }

        if (warmupEventCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warmupEventCount), warmupEventCount, "Warmup event count cannot be negative");
        }

        _configuredEventCount = eventCount;
        _delayBetweenEvents = delayBetweenEvents ?? TimeSpan.Zero;
        _warmupEventCount = warmupEventCount;

        _output?.WriteLine(
            $"ConfigurableReferenceService configured: {_configuredEventCount} event(s), " +
            $"{_delayBetweenEvents.TotalSeconds:F2}s delay, ignoring first {_warmupEventCount} warmup event(s)");
    }

    /// <summary>
    /// Configure the HTTP response status code for /kpi endpoint.
    /// All requests will return this status code.
    /// </summary>
    /// <param name="statusCode">HTTP status code to return (default: 200)</param>
    public void ConfigureHttpResponse(int statusCode = 200)
    {
        _httpStatusCode = statusCode;
        _failEveryNthRequest = 0;
        _kpiRequestCount = 0;

        _output?.WriteLine(
            $"ConfigurableReferenceService HTTP configured: always return status {_httpStatusCode}");
    }

    /// <summary>
    /// Configure intermittent failures (fail every Nth request).
    /// Requests not matching Nth pattern return 200 OK.
    /// </summary>
    /// <param name="failEveryNthRequest">Fail every Nth request (e.g., 3 = fail every 3rd request)</param>
    public void ConfigureIntermittentFailures(int failEveryNthRequest)
    {
        if (failEveryNthRequest < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failEveryNthRequest),
                failEveryNthRequest,
                "Must be at least 1");
        }

        _failEveryNthRequest = failEveryNthRequest;
        _httpStatusCode = 200; // Success by default
        _kpiRequestCount = 0;

        _output?.WriteLine(
            $"ConfigurableReferenceService HTTP configured: fail every {_failEveryNthRequest} request(s)");
    }

    /// <summary>
    /// Configure service to publish warmup events only, no test events.
    /// Use for testing warmup timeout scenarios where warmup succeeds but test events never arrive.
    /// </summary>
    /// <param name="warmupEventCount">Number of warmup events to publish (then stop)</param>
    public void ConfigureWarmupOnly(int warmupEventCount)
    {
        if (warmupEventCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warmupEventCount), warmupEventCount, "Warmup event count cannot be negative");
        }

        _warmupEventCount = warmupEventCount;
        _configuredEventCount = 0;
        _publishMode = PublishMode.WarmupOnly;

        _output?.WriteLine(
            $"ConfigurableReferenceService configured: WarmupOnly mode - {_warmupEventCount} warmup event(s), then stop publishing");
    }

    /// <summary>
    /// Configure service to terminate (disconnect) after processing N events.
    /// Use for testing process death scenarios during event processing.
    /// </summary>
    /// <param name="eventCount">Total events to process before termination (includes warmup events)</param>
    public void ConfigureTerminationAfterEvents(int eventCount)
    {
        if (eventCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(eventCount), eventCount, "Event count must be at least 1");
        }

        _terminateAfterEvents = eventCount;
        _terminationEnabled = true;

        _output?.WriteLine(
            $"ConfigurableReferenceService configured: will terminate after {_terminateAfterEvents} event(s)");
    }

    /// <summary>
    /// Configure delay between publishing each event.
    /// Use for testing slow processing scenarios.
    /// </summary>
    /// <param name="delayBetweenEvents">Delay between each published event</param>
    public void ConfigurePublishDelay(TimeSpan delayBetweenEvents)
    {
        if (delayBetweenEvents < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delayBetweenEvents), delayBetweenEvents, "Delay cannot be negative");
        }

        _delayBetweenEvents = delayBetweenEvents;

        _output?.WriteLine(
            $"ConfigurableReferenceService configured: {_delayBetweenEvents.TotalSeconds:F2}s delay between events");
    }

    /// <summary>
    /// Configure service to not respond to any events (complete silence).
    /// Use for testing complete timeout scenarios where service ACKs but never publishes.
    /// </summary>
    public void ConfigureNoResponse()
    {
        _publishMode = PublishMode.NoResponse;
        _configuredEventCount = 0;
        _warmupEventCount = 0;

        _output?.WriteLine(
            $"ConfigurableReferenceService configured: NoResponse mode - will ACK events but never publish");
    }

    /// <summary>
    /// Connects to RabbitMQ and subscribes to input events.
    /// Optionally starts an HTTP listener for service discovery.
    /// </summary>
    /// <param name="listenPort">Optional port to listen on for service discovery (makes this publisher discoverable)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task ConnectAndSubscribeAsync(int? listenPort = null, CancellationToken cancellationToken = default)
    {
        if (_isConnected)
        {
            _output?.WriteLine("⚠️ ConfigurableReferenceService already connected");
            return;
        }

        _output?.WriteLine("ConfigurableReferenceService connecting to RabbitMQ...");

        // Create connection
        var factory = new ConnectionFactory { Uri = new Uri(_connectionString) };
        _connection = await factory.CreateConnectionAsync(cancellationToken);

        // Create input channel (for consuming)
        _inputChannel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        // Create output channel (for publishing)
        _outputChannel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        _output?.WriteLine("✓ ConfigurableReferenceService connected to RabbitMQ");

        // Declare exchange (idempotent)
        await _inputChannel.ExchangeDeclareAsync(
            exchange: _exchangeName,
            type: "topic",
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        // Declare input queue
        await _inputChannel.QueueDeclareAsync(
            queue: _inputQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        // Bind input queue to exchange
        await _inputChannel.QueueBindAsync(
            queue: _inputQueueName,
            exchange: _exchangeName,
            routingKey: _inputRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken);

        _output?.WriteLine(
            $"✓ Input queue '{_inputQueueName}' bound to exchange '{_exchangeName}' " +
            $"with routing key '{_inputRoutingKey}'");

        // Create async consumer
        var consumer = new AsyncEventingBasicConsumer(_inputChannel);
        consumer.ReceivedAsync += OnInputEventReceivedAsync;

        // Start consuming
        await _inputChannel.BasicConsumeAsync(
            queue: _inputQueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        _output?.WriteLine("✓ ConfigurableReferenceService listening for input events...");

        // Start HTTP listener if port specified (for service discovery)
        if (listenPort.HasValue)
        {
            // Retry logic to handle port release timing issues between tests
            const int maxRetries = 5;
            const int retryDelayMs = 100;
            bool started = false;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Create new HttpListener on each attempt (previous one may be disposed)
                    _httpListener = new HttpListener();
                    // Use http://+: to bind to all interfaces (localhost may not match 127.0.0.1 in some scenarios)
                    _httpListener.Prefixes.Add($"http://+:{listenPort.Value}/");
                    _httpListener.Start();
                    started = true;
                    break;
                }
                catch (HttpListenerException ex) when (ex.Message.Contains("Address already in use"))
                {
                    // Dispose failed listener before retrying
                    _httpListener?.Close();
                    _httpListener = null;
                    
                    if (attempt == maxRetries)
                    {
                        _output?.WriteLine($"❌ Failed to start HTTP listener on port {listenPort.Value} after {maxRetries} attempts");
                        throw;
                    }
                    
                    _output?.WriteLine($"⚠️ Port {listenPort.Value} in use (attempt {attempt}/{maxRetries}), retrying in {retryDelayMs * attempt}ms...");
                    await Task.Delay(retryDelayMs * attempt, cancellationToken); // Exponential backoff
                }
            }
            
            if (started)
            {
                _output?.WriteLine($"✓ HTTP listener started on port {listenPort.Value} (discoverable by ServiceDiscovery)");

                // Run background task to handle HTTP requests
                _httpListenerTask = Task.Run(async () =>
                {
                    while (_httpListener!.IsListening && !_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        try
                        {
                            var context = await _httpListener.GetContextAsync();

                            // Route based on request path
                            if (context.Request.Url?.AbsolutePath == "/health")
                            {
                                // Health check endpoint - returns readiness state
                                if (_isConnected)
                                {
                                    context.Response.StatusCode = 200;
                                    context.Response.ContentType = "application/json";
                                    await using var writer = new StreamWriter(context.Response.OutputStream);
                                    await writer.WriteAsync("{\"status\":\"ready\",\"rabbitMq\":\"connected\"}");
                                }
                                else
                                {
                                    context.Response.StatusCode = 503;
                                    context.Response.ContentType = "application/json";
                                    await using var writer = new StreamWriter(context.Response.OutputStream);
                                    await writer.WriteAsync("{\"status\":\"not ready\",\"rabbitMq\":\"disconnected\"}");
                                }
                            }
                            else if (context.Request.Url?.AbsolutePath == "/kpi")
                            {
                                // KPI endpoint - returns mock data (matches reference service specification)
                                // Real reference services return the latest record from PostgreSQL

                                // Determine status code based on configuration
                                int statusCode;
                                if (_failEveryNthRequest > 0)
                                {
                                    // Intermittent failure mode: fail every Nth request
                                    var requestNum = Interlocked.Increment(ref _kpiRequestCount);
                                    statusCode = (requestNum % _failEveryNthRequest == 0) ? 500 : 200;
                                }
                                else
                                {
                                    // Fixed status code mode
                                    statusCode = _httpStatusCode;
                                }

                                context.Response.StatusCode = statusCode;
                                context.Response.ContentType = "application/json";
                                await using var writer = new StreamWriter(context.Response.OutputStream);

                                if (statusCode >= 400)
                                {
                                    // Error response
                                    await writer.WriteAsync("{\"error\":\"Configured test error\",\"statusCode\":" + statusCode + "}");
                                }
                                else
                                {
                                    // Success response
                                    await writer.WriteAsync("{\"deviceId\":\"TEST-DEVICE-001\",\"currentStatus\":\"RUNNING\",\"timestamp\":\"" + DateTimeOffset.UtcNow.ToString("o") + "\"}");
                                }
                            }
                            else
                            {
                                // Root endpoint - backward compatibility for service discovery
                                context.Response.StatusCode = 200;
                                context.Response.ContentType = "text/plain";
                                await using var writer = new StreamWriter(context.Response.OutputStream);
                                await writer.WriteAsync("ConfigurableReferenceService");
                            }

                            context.Response.Close();

                            _output?.WriteLine($"  → HTTP {context.Request.HttpMethod} {context.Request.Url?.AbsolutePath} → {context.Response.StatusCode}");
                        }
                        catch (HttpListenerException)
                        {
                            // Listener stopped - expected during shutdown
                            break;
                        }
                        catch (Exception ex)
                        {
                            _output?.WriteLine($"⚠️ HTTP listener error: {ex.Message}");
                        }
                    }

                    _output?.WriteLine("✓ HTTP listener task completed");
                }, _cancellationTokenSource.Token);
            }
        }

        _isConnected = true;
        _triggerCompletionSource = new TaskCompletionSource<bool>();
        _publicationCompletionSource = new TaskCompletionSource<bool>();
    }

    /// <summary>
    /// Disconnects from RabbitMQ and stops HTTP listener.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return;
        }

        _output?.WriteLine("ConfigurableReferenceService disconnecting...");

        // Cancel any ongoing operations
        await _cancellationTokenSource.CancelAsync();

        // Stop HTTP listener
        if (_httpListener != null)
        {
            _httpListener.Stop();
            _httpListener.Close();

            // IMPORTANT: Must dispose to release port binding immediately
            // Without this, the OS may keep the port in TIME_WAIT state
            ((IDisposable)_httpListener).Dispose();
            _httpListener = null;
            _output?.WriteLine("✓ HTTP listener stopped and disposed");
        }

        // Wait for HTTP listener task to complete
        if (_httpListenerTask != null)
        {
            try
            {
                await _httpListenerTask;
                _output?.WriteLine("✓ HTTP listener task completed");
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation token is triggered
            }
            catch (Exception ex)
            {
                _output?.WriteLine($"⚠️ HTTP listener task exception: {ex.Message}");
            }
            _httpListenerTask = null;
        }

        // Close channels
        if (_inputChannel != null)
        {
            await _inputChannel.CloseAsync(cancellationToken);
            _inputChannel.Dispose();
            _inputChannel = null;
        }

        if (_outputChannel != null)
        {
            await _outputChannel.CloseAsync(cancellationToken);
            _outputChannel.Dispose();
            _outputChannel = null;
        }

        // Close connection
        if (_connection != null)
        {
            await _connection.CloseAsync(cancellationToken);
            _connection.Dispose();
            _connection = null;
        }

        _isConnected = false;
        _output?.WriteLine("✓ ConfigurableReferenceService disconnected");
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _cancellationTokenSource.Dispose();
    }

    private async Task OnInputEventReceivedAsync(object sender, BasicDeliverEventArgs eventArgs)
    {
        try
        {
            // Increment input counter
            var inputCount = Interlocked.Increment(ref _receivedInputEventCount);

            // Check for termination (simulates process death)
            if (_terminationEnabled && inputCount >= _terminateAfterEvents)
            {
                _output?.WriteLine($"💀 ConfigurableReferenceService terminating after {inputCount} events (simulating process death)");

                // ACK this last event before terminating
                if (_inputChannel != null)
                {
                    await _inputChannel.BasicAckAsync(deliveryTag: eventArgs.DeliveryTag, multiple: false);
                }

                // Disconnect to simulate process death
                _ = Task.Run(async () => await DisconnectAsync(), CancellationToken.None);
                return;
            }

            // Handle based on publish mode
            switch (_publishMode)
            {
                case PublishMode.NoResponse:
                    // ACK but don't publish anything - complete silence
                    _output?.WriteLine($"  ⊙ NoResponse mode: ACK event {inputCount} without publishing");
                    break;

                case PublishMode.WarmupOnly:
                    // Only publish during warmup phase
                    if (inputCount <= _warmupEventCount)
                    {
                        _output?.WriteLine($"  ⊙ WarmupOnly mode: warmup event {inputCount}/{_warmupEventCount} - publishing");
                        await PublishSingleEventAsync(inputCount);
                    }
                    else
                    {
                        _output?.WriteLine($"  ⊙ WarmupOnly mode: post-warmup event {inputCount} - NOT publishing (warmup complete)");
                    }
                    break;

                case PublishMode.Normal:
                default:
                    // Warmup events (1 through _warmupEventCount): Publish immediately (act like real service)
                    if (inputCount <= _warmupEventCount)
                    {
                        _output?.WriteLine($"  ⊙ Warmup event {inputCount}/{_warmupEventCount} - publishing immediately");

                        // Publish one event immediately for each warmup event
                        await PublishSingleEventAsync(inputCount);
                    }
                    // First event AFTER warmup triggers configured publication behavior
                    else if (inputCount == _warmupEventCount + 1)
                    {
                        _output?.WriteLine($"✓ ConfigurableReferenceService received trigger event {inputCount} (first after warmup, ID: {eventArgs.DeliveryTag})");
                        _triggerCompletionSource?.TrySetResult(true);

                        // Start publishing configured number of output events (with optional delay)
                        _ = Task.Run(async () => await PublishOutputEventsAsync(), _cancellationTokenSource.Token);
                    }
                    else
                    {
                        // Ignore subsequent input events after trigger (just ACK them)
                        _output?.WriteLine($"  ⊙ Ignoring input event {inputCount} (already triggered)");
                    }
                    break;
            }

            // Always ACK input events
            if (_inputChannel != null)
            {
                await _inputChannel.BasicAckAsync(deliveryTag: eventArgs.DeliveryTag, multiple: false);
            }
        }
        catch (Exception ex)
        {
            _output?.WriteLine($"❌ Error processing input event: {ex.Message}");

            // NACK and requeue
            if (_inputChannel != null)
            {
                await _inputChannel.BasicNackAsync(
                    deliveryTag: eventArgs.DeliveryTag,
                    multiple: false,
                    requeue: true);
            }
        }
    }

    private async Task PublishSingleEventAsync(int eventNumber)
    {
        try
        {
            // Create KPI CloudEvent
            var cloudEvent = CreateKpiEvent(eventNumber);

            // Serialize and publish
            var messageBytes = _formatter.EncodeStructuredModeMessage(cloudEvent, out var contentType);
            var properties = new BasicProperties
            {
                ContentType = contentType.ToString(),
                DeliveryMode = DeliveryModes.Persistent
            };

            await _outputChannel!.BasicPublishAsync(
                exchange: _exchangeName,
                routingKey: _outputRoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: messageBytes.ToArray(),
                cancellationToken: _cancellationTokenSource.Token);

            _output?.WriteLine($"    ✓ Published warmup event (ID: {cloudEvent.Id})");
        }
        catch (Exception ex)
        {
            _output?.WriteLine($"❌ Error publishing warmup event: {ex.Message}");
        }
    }

    private async Task PublishOutputEventsAsync()
    {
        try
        {
            _output?.WriteLine(
                $"ConfigurableReferenceService publishing {_configuredEventCount} KPI event(s) " +
                $"with {_delayBetweenEvents.TotalSeconds:F2}s delay...");

            for (int i = 0; i < _configuredEventCount; i++)
            {
                _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                // Create KPI CloudEvent using shared library
                var cloudEvent = CreateKpiEvent(eventNumber: i + 1);

                // Serialize and publish
                var messageBytes = _formatter.EncodeStructuredModeMessage(cloudEvent, out var contentType);
                var properties = new BasicProperties
                {
                    ContentType = contentType.ToString(),
                    DeliveryMode = DeliveryModes.Persistent
                };

                await _outputChannel!.BasicPublishAsync(
                    exchange: _exchangeName,
                    routingKey: _outputRoutingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: messageBytes.ToArray(),
                    cancellationToken: _cancellationTokenSource.Token);

                _output?.WriteLine($"  ✓ Published KPI event {i + 1}/{_configuredEventCount} (ID: {cloudEvent.Id})");

                // Delay before next event (except last one)
                if (i < _configuredEventCount - 1 && _delayBetweenEvents > TimeSpan.Zero)
                {
                    await Task.Delay(_delayBetweenEvents, _cancellationTokenSource.Token);
                }
            }

            _output?.WriteLine($"✓ ConfigurableReferenceService published {_configuredEventCount} KPI event(s)");
            _publicationCompletionSource?.TrySetResult(true);
        }
        catch (Exception ex)
        {
            _output?.WriteLine($"❌ Publication failed: {ex.Message}");
            _publicationCompletionSource?.TrySetException(ex);
        }
    }

    private static CloudEvent CreateKpiEvent(int eventNumber)
    {
        // Create CloudEvent with same schema as real services (InstrumentStatusKpiUpdatedEvent)
        // This ensures compatibility with services using Dotnet9.Events library
        return new CloudEvent
        {
            Id = Guid.NewGuid().ToString(),
            Source = new Uri("urn:uuid:configurable-event-publisher-test"),
            Type = "instrumentstatus.kpi.updated",
            Time = DateTimeOffset.UtcNow,
            DataContentType = "application/json",
            Data = new Dictionary<string, object>
            {
                { "deviceId", $"TEST-DEVICE-{eventNumber:D3}" },
                { "currentStatus", "RUNNING" }
            }
        };
    }
}
