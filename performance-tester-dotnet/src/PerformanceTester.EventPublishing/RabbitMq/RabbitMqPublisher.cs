using CloudNative.CloudEvents;
using Microsoft.Extensions.Logging;
using PerformanceTester.EventPublishing.CloudEvents;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;

namespace PerformanceTester.EventPublishing.RabbitMq;

/// <summary>
/// Thin shell for RabbitMQ connection management and message publishing.
/// Owns the mutable context and delegates logic to static PublisherOperations.
/// Call ConnectAsync() before publishing, DisconnectAsync() for graceful shutdown.
/// </summary>
internal sealed class RabbitMqPublisher : IAsyncDisposable
{
    private readonly PublisherContext _ctx = new();
    private readonly RabbitMqConnectionString _connectionString;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    /// <summary>
    /// Initializes a new instance of RabbitMqPublisher.
    /// </summary>
    /// <param name="connectionString">RabbitMQ connection string.</param>
    /// <param name="logger">Logger instance.</param>
    public RabbitMqPublisher(RabbitMqConnectionString connectionString, ILogger<RabbitMqPublisher> logger)
    {
        _connectionString = connectionString;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Configure Polly retry policy for transient failures
        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (
                    exception,
                    timeSpan,
                    retryCount,
                    context) =>
                {
                    _logger.LogWarning(
                        exception,
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
    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        PublisherOperations.ConnectAsync(_ctx, _connectionString, _logger, cancellationToken);

    /// <summary>
    /// Gracefully disconnects from RabbitMQ.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        PublisherOperations.DisconnectAsync(_ctx, _logger, cancellationToken);

    /// <summary>
    /// Publishes a CloudEvent to RabbitMQ with retry logic.
    /// Uses the persistent channel created during ConnectAsync.
    /// Requires ConnectAsync() to be called first.
    /// </summary>
    /// <param name="cloudEvent">CloudEvent to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Not connected (call ConnectAsync first).</exception>
    public async Task PublishEventAsync(CloudEvent cloudEvent, CancellationToken cancellationToken = default)
    {
        await _retryPolicy.ExecuteAsync(async () =>
        {
            var channel = PublisherOperations.EnsureConnected(_ctx);

            // Serialize via shared factory (G3 — no single-use wrapper)
            var body = CloudEventFactory.Serialize(cloudEvent);

            var properties = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent,
                ContentType = "application/json"
            };

            // Publish message on persistent channel
            await channel.BasicPublishAsync(
                exchange: PublisherOperations.ExchangeName,
                routingKey: PublisherOperations.RoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        });
    }

    /// <summary>
    /// Publishes a pre-serialized message directly to the channel without retry wrapping.
    /// Returns the raw ValueTask for pipelining (collect and await in batches).
    /// Requires ConnectAsync() to be called first.
    /// </summary>
    public ValueTask PublishDirectAsync(
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        var channel = PublisherOperations.EnsureConnected(_ctx);

        return channel.BasicPublishAsync(
            exchange: PublisherOperations.CachedExchangeName,
            routingKey: PublisherOperations.CachedRoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Disposes the publisher and closes the connection.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _ctx.ConnectionLock.Dispose();
    }
}
