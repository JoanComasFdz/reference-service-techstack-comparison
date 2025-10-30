using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Dotnet9.Events;

namespace Dotnet9ReferenceService;

/// <summary>
/// Background service that manages RabbitMQ connections and message processing.
/// Consumes instrument status changed events and publishes KPI updated events.
/// </summary>
public class RabbitMqService : BackgroundService
{
    private const string ExchangeName = "referenceservice.comparison";
    private const string QueueName = "dotnet9";
    private const string RoutingKeyStatusChanged = "instrument.status.changed";
    private const string RoutingKeyKpiUpdated = "instrumentstatus.kpi.updated";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<RabbitMqService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private IConnection? _connection;
    private IChannel? _channel;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="serviceProvider">The service provider for creating scoped services.</param>
    /// <param name="configuration">The application configuration.</param>
    public RabbitMqService(ILogger<RabbitMqService> logger, IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    /// <summary>
    /// Executes the background service tasks, initializing RabbitMQ and consuming messages.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for stopping the service.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeRabbitMq(stoppingToken);
        await ConsumeMessages(stoppingToken);
    }

    /// <summary>
    /// Initializes the RabbitMQ connection, channel, exchange, and queue bindings.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for stopping the initialization.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task InitializeRabbitMq(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _configuration.GetValue<string>("RabbitMQ:Host", "localhost"),
            Port = _configuration.GetValue<int>("RabbitMQ:Port", 5672),
            UserName = _configuration.GetValue<string>("RabbitMQ:Username", "admin"),
            Password = _configuration.GetValue<string>("RabbitMQ:Password", "admin")
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        // Declare exchange
        await _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        // Declare queue
        await _channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        // Bind queue to exchange with routing key
        await _channel.QueueBindAsync(
            queue: QueueName,
            exchange: ExchangeName,
            routingKey: RoutingKeyStatusChanged,
            cancellationToken: cancellationToken);

        // Set prefetch count (QoS) from configuration
        var prefetchCount = _configuration.GetValue<ushort>("RabbitMQ:PrefetchCount", 50);
        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: prefetchCount,
            global: false,
            cancellationToken: cancellationToken);

        _logger.LogInformation("RabbitMQ initialized: Exchange={Exchange}, Queue={Queue}, Prefetch={Prefetch}", ExchangeName, QueueName, prefetchCount);
    }

    /// <summary>
    /// Consumes messages from the RabbitMQ queue and processes them.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for stopping message consumption.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ConsumeMessages(CancellationToken stoppingToken)
    {
        if (_channel == null)
        {
            _logger.LogError("Channel is not initialized");
            return;
        }

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                await ProcessMessage(message);

                // Acknowledge after processing completes
                await _channel!.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                // Reject and requeue on error
                await _channel!.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        await _channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Started consuming messages from queue: {Queue}", QueueName);
    }

    /// <summary>
    /// Processes a received message by deserializing it, saving to database, and publishing a KPI event.
    /// </summary>
    /// <param name="message">The JSON message to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessMessage(string message)
    {
        var statusChangedEvent = JsonSerializer.Deserialize<InstrumentStatusChangedEvent>(message, JsonOptions);
        if (statusChangedEvent == null)
        {
            _logger.LogWarning("Failed to deserialize message");
            return;
        }

        _logger.LogInformation("Received event: {Type} from {Source}", statusChangedEvent.Type, statusChangedEvent.Source);

        // Extract data using typed methods
        var deviceId = statusChangedEvent.GetDeviceId();
        var previousStatus = statusChangedEvent.GetPreviousStatus();
        var currentStatus = statusChangedEvent.GetCurrentStatus();

        _logger.LogInformation("Processing status change for device: {DeviceId} from {PreviousStatus} to {CurrentStatus}",
            deviceId, previousStatus, currentStatus);

        // Save to database
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var instrumentStatus = new InstrumentStatus
        {
            DeviceId = deviceId ?? string.Empty,
            PreviousStatus = previousStatus ?? string.Empty,
            CurrentStatus = currentStatus ?? string.Empty,
            Timestamp = DateTime.UtcNow
        };

        dbContext.InstrumentStatuses.Add(instrumentStatus);
        await dbContext.SaveChangesAsync();

        _logger.LogInformation("Saved instrument status to database with ID: {Id}", instrumentStatus.Id);

        // Publish KPI event
        await PublishKpiEvent(deviceId ?? string.Empty, currentStatus ?? string.Empty);
    }

    /// <summary>
    /// Publishes a KPI updated event to RabbitMQ.
    /// </summary>
    /// <param name="deviceId">The device identifier.</param>
    /// <param name="currentStatus">The current status of the instrument.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task PublishKpiEvent(string deviceId, string currentStatus)
    {
        if (_channel == null)
        {
            _logger.LogError("Channel is not initialized");
            return;
        }

        try
        {
            var kpiEvent = new InstrumentStatusKpiUpdatedEvent(
                source: "urn:uuid:dotnet9",
                deviceId: deviceId,
                currentStatus: currentStatus
            );

            var messageBody = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(kpiEvent));

            await _channel.BasicPublishAsync(
                exchange: ExchangeName,
                routingKey: RoutingKeyKpiUpdated,
                body: messageBody);

            _logger.LogInformation("Published KPI event for device: {DeviceId}", deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing KPI event");
        }
    }

    /// <summary>
    /// Stops the background service and cleanly closes RabbitMQ connections.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for stopping the service.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel != null)
        {
            await _channel.CloseAsync(cancellationToken);
            await _channel.DisposeAsync();
        }

        if (_connection != null)
        {
            await _connection.CloseAsync(cancellationToken);
            await _connection.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
