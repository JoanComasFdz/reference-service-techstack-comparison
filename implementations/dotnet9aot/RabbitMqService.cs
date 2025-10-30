using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Dotnet9.Events;
using System.Diagnostics.CodeAnalysis;

namespace Dotnet9ReferenceServiceAoT;

public class RabbitMqService : BackgroundService
{
    private const string ExchangeName = "referenceservice.comparison";
    private const string QueueName = "dotnet9aot";
    private const string RoutingKeyStatusChanged = "instrument.status.changed";
    private const string RoutingKeyKpiUpdated = "instrumentstatus.kpi.updated";

    private readonly ILogger<RabbitMqService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqService(ILogger<RabbitMqService> logger, IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeRabbitMq(stoppingToken);
        await ConsumeMessages(stoppingToken);
    }

    private async Task InitializeRabbitMq(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _configuration["RabbitMQ:Host"] ?? "localhost",
            Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
            UserName = _configuration["RabbitMQ:Username"] ?? "admin",
            Password = _configuration["RabbitMQ:Password"] ?? "admin"
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
        var prefetchCount = ushort.Parse(_configuration["RabbitMQ:PrefetchCount"] ?? "50");
        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: prefetchCount,
            global: false,
            cancellationToken: cancellationToken);

        _logger.LogInformation("RabbitMQ initialized: Exchange={Exchange}, Queue={Queue}, Prefetch={Prefetch}", ExchangeName, QueueName, prefetchCount);
    }

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
            autoAck: false,  // Manual acknowledgment for proper backpressure
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Started consuming messages from queue: {Queue}", QueueName);
    }

    private async Task ProcessMessage(string message)
    {
        var statusChangedEvent = JsonSerializer.Deserialize(message, AppJsonSerializerContext.Default.InstrumentStatusChangedEvent);
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
        var repository = scope.ServiceProvider.GetRequiredService<IInstrumentStatusRepository>();

        var instrumentStatus = new InstrumentStatus
        {
            DeviceId = deviceId!,
            PreviousStatus = previousStatus!,
            CurrentStatus = currentStatus!,
            Timestamp = DateTime.UtcNow
        };

        var id = await repository.AddAsync(instrumentStatus);

        _logger.LogInformation("Saved instrument status to database with ID: {Id}", id);

        // Publish KPI event
        await PublishKpiEvent(deviceId!, currentStatus!);
    }

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
                source: "urn:uuid:dotnet9aot",
                deviceId: deviceId,
                currentStatus: currentStatus
            );

            var messageBody = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(kpiEvent, AppJsonSerializerContext.Default.InstrumentStatusKpiUpdatedEvent));

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
