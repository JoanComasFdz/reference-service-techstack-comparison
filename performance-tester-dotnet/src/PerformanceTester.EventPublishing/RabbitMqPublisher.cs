using CloudNative.CloudEvents;
using CloudNative.CloudEvents.SystemTextJson;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using System.Text;

namespace PerformanceTester.EventPublishing;

/// <summary>
/// Handles RabbitMQ connection management and message publishing.
/// Uses single persistent connection (expensive) with per-operation channels (cheap).
/// Call ConnectAsync() before publishing, DisconnectAsync() for graceful shutdown.
/// </summary>
internal sealed class RabbitMqPublisher : IAsyncDisposable
{
    private const string ExchangeName = "referenceservice.comparison";
    private const string RoutingKey = "instrument.status.changed";

    private readonly string _connectionString;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly CloudEventFormatter _formatter;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private IConnection? _connection;
    private bool _isConnected;

    /// <summary>
    /// Initializes a new instance of RabbitMqPublisher.
    /// </summary>
    /// <param name="connectionString">RabbitMQ connection string.</param>
    /// <param name="logger">Logger instance.</param>
    public RabbitMqPublisher(string connectionString, ILogger<RabbitMqPublisher> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _formatter = new JsonEventFormatter();

        // Configure Polly retry policy for transient failures
        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(exception,
                        "Publish attempt {RetryCount} failed, retrying in {DelaySeconds}s",
                        retryCount,
                        timeSpan.TotalSeconds);
                });
    }

    /// <summary>
    /// Establishes connection to RabbitMQ.
    /// Must be called before PublishEventAsync.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Already connected.</exception>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_isConnected)
            {
                _logger.LogWarning("Already connected to RabbitMQ");
                return;
            }

            _logger.LogInformation("Connecting to RabbitMQ at {ConnectionString}", MaskConnectionString(_connectionString));

            var factory = new ConnectionFactory { Uri = new Uri(_connectionString) };
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _isConnected = true;

            _logger.LogInformation("RabbitMQ connection established");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to RabbitMQ");
            throw new InvalidOperationException($"Failed to connect to RabbitMQ: {ex.Message}", ex);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Gracefully disconnects from RabbitMQ.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (!_isConnected || _connection == null)
            {
                _logger.LogDebug("Not connected, nothing to disconnect");
                return;
            }

            _logger.LogInformation("Disconnecting from RabbitMQ");

            await _connection.CloseAsync(cancellationToken);
            _connection.Dispose();
            _connection = null;
            _isConnected = false;

            _logger.LogInformation("RabbitMQ connection closed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during RabbitMQ disconnect");
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Publishes a CloudEvent to RabbitMQ with retry logic.
    /// Creates a new channel per operation (disposable).
    /// Requires ConnectAsync() to be called first.
    /// </summary>
    /// <param name="cloudEvent">CloudEvent to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Not connected (call ConnectAsync first).</exception>
    public async Task PublishEventAsync(CloudEvent cloudEvent, CancellationToken cancellationToken = default)
    {
        await _retryPolicy.ExecuteAsync(async () =>
        {
            EnsureConnected();

            await using var channel = await _connection!.CreateChannelAsync(cancellationToken: cancellationToken);

            // Declare exchange (idempotent)
            await channel.ExchangeDeclareAsync(
                exchange: ExchangeName,
                type: "topic",
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            // Serialize CloudEvent to JSON
            var jsonBytes = SerializeCloudEvent(cloudEvent);

            // Create message properties
            var properties = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent,
                ContentType = "application/cloudevents+json"
            };

            // Publish message
            await channel.BasicPublishAsync(
                exchange: ExchangeName,
                routingKey: RoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: jsonBytes,
                cancellationToken: cancellationToken);
        });
    }

    private void EnsureConnected()
    {
        if (!_isConnected || _connection == null)
        {
            throw new InvalidOperationException(
                "Not connected to RabbitMQ. Call ConnectAsync() before publishing.");
        }
    }

    private byte[] SerializeCloudEvent(CloudEvent cloudEvent)
    {
        // Use CloudEvents JSON formatter for v1.0 compliance
        var jsonBytes = _formatter.EncodeStructuredModeMessage(cloudEvent, out var contentType);
        return jsonBytes.ToArray();
    }

    private static string MaskConnectionString(string connectionString)
    {
        var uri = new Uri(connectionString);
        var userInfo = !string.IsNullOrEmpty(uri.UserInfo) ? "***:***" : "";
        return $"{uri.Scheme}://{userInfo}@{uri.Host}:{uri.Port}";
    }

    /// <summary>
    /// Disposes the publisher and closes the connection.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _connectionLock.Dispose();
    }
}
