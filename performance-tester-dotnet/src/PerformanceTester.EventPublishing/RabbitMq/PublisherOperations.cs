using Microsoft.Extensions.Logging;
using PerformanceTester.EventPublishing;
using RabbitMQ.Client;

namespace PerformanceTester.EventPublishing.RabbitMq;

/// <summary>
/// Publishes a pre-serialized message directly to a channel without retry wrapping.
/// Returns a raw ValueTask for pipelining (collect and await in batches).
/// </summary>
internal delegate ValueTask PublishDirectDelegate(
    BasicProperties properties,
    ReadOnlyMemory<byte> body,
    CancellationToken cancellationToken = default);

/// <summary>
/// Static module for RabbitMQ connection management and message publishing (G30).
/// All state lives in PublisherContext, created and closed over by BuildDependencies.
/// </summary>
internal static class PublisherOperations
{
    internal const string ExchangeName = "referenceservice.comparison";
    internal const string RoutingKey = "instrument.status.changed";

    internal static readonly CachedString CachedExchangeName = new(ExchangeName);
    internal static readonly CachedString CachedRoutingKey = new(RoutingKey);

    // -- Dependencies record (G30) ------------------------------------------------

    /// <summary>
    /// Bundled publisher delegates with context pre-bound.
    /// Built by <see cref="BuildDependencies"/> and consumed by DI registration.
    /// </summary>
    internal record Dependencies(
        ConnectPublisherDelegate Connect,
        DisconnectPublisherDelegate Disconnect,
        PublishDirectDelegate PublishDirect);

    // -- Factory (G30) ------------------------------------------------------------

    /// <summary>
    /// Creates publisher context and binds all operations into delegates.
    /// Connection string and logger are baked in (G31 — immutable at construction time).
    /// </summary>
    public static Dependencies BuildDependencies(
        RabbitMqConnectionString connectionString,
        ILogger logger)
    {
        var ctx = new PublisherContext();

        return new Dependencies(
            Connect: (ct) => ConnectAsync(ctx, connectionString, logger, ct),
            Disconnect: (ct) => DisconnectAsync(ctx, logger, ct),
            PublishDirect: (properties, body, ct) => PublishDirectAsync(ctx, properties, body, ct));
    }

    // -- Operations ---------------------------------------------------------------

    public static async Task ConnectAsync(
        PublisherContext ctx,
        RabbitMqConnectionString connectionString,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await ctx.ConnectionLock.WaitAsync(cancellationToken);
        try
        {
            if (ctx.IsConnected)
            {
                logger.LogWarning("Already connected to RabbitMQ");
                return;
            }

            logger.LogInformation(
                "Connecting to RabbitMQ at {ConnectionString}",
                connectionString.ToMaskedString());

            // Build connection + channel as locals; only store atomically on success
            var factory = new ConnectionFactory { Uri = connectionString.ToUri() };
            var connection = await factory.CreateConnectionAsync(cancellationToken);
            IChannel? channel = null;

            try
            {
                // Create persistent channel for publishing
                channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

                // Declare exchange once (idempotent)
                await channel.ExchangeDeclareAsync(
                    exchange: ExchangeName,
                    type: "topic",
                    durable: true,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: cancellationToken);

                // Atomic store — context transitions from disconnected to connected
                ctx.ActiveConnection = new ConnectionState(connection, channel);

                logger.LogInformation(
                    "RabbitMQ publisher channel created and exchange '{ExchangeName}' declared",
                    ExchangeName);
            }
            catch
            {
                // Cleanup partially created resources (locals, not context)
                if (channel != null)
                {
                    try { await channel.CloseAsync(cancellationToken); } catch { /* ignore cleanup errors */ }
                    channel.Dispose();
                }

                try { await connection.CloseAsync(cancellationToken); } catch { /* ignore cleanup errors */ }
                connection.Dispose();

                throw;
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            logger.LogError(ex, "Failed to connect to RabbitMQ");
            throw new InvalidOperationException($"Failed to connect to RabbitMQ: {ex.Message}", ex);
        }
        finally
        {
            ctx.ConnectionLock.Release();
        }
    }

    public static async Task DisconnectAsync(
        PublisherContext ctx,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await ctx.ConnectionLock.WaitAsync(cancellationToken);
        try
        {
            if (ctx.ActiveConnection == null)
            {
                logger.LogDebug("Not connected, nothing to disconnect");
                return;
            }

            logger.LogInformation("Disconnecting from RabbitMQ");

            // Destructure and clear atomically
            var (connection, channel) = ctx.ActiveConnection;
            ctx.ActiveConnection = null;

            await channel.CloseAsync(cancellationToken);
            channel.Dispose();

            await connection.CloseAsync(cancellationToken);
            connection.Dispose();

            logger.LogInformation("RabbitMQ connection closed");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error during RabbitMQ disconnect");
        }
        finally
        {
            ctx.ConnectionLock.Release();
        }
    }

    /// <summary>
    /// Returns the channel if connected; throws if not.
    /// Callers use the returned channel — no null-forgiving operators needed.
    /// </summary>
    public static IChannel EnsureConnected(PublisherContext ctx) =>
        ctx.ActiveConnection?.Channel
        ?? throw new InvalidOperationException(
            "Not connected to RabbitMQ. Call ConnectAsync() before publishing.");

    /// <summary>
    /// Publishes a pre-serialized message directly to the channel without retry wrapping.
    /// Returns the raw ValueTask for pipelining (collect and await in batches).
    /// </summary>
    public static ValueTask PublishDirectAsync(
        PublisherContext ctx,
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        var channel = EnsureConnected(ctx);

        return channel.BasicPublishAsync(
            exchange: CachedExchangeName,
            routingKey: CachedRoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }
}
