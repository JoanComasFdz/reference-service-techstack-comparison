using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace PerformanceTester.EventPublishing;

/// <summary>
/// Static operations for RabbitMQ connection management.
/// All state is passed explicitly via PublisherContext (G34 — Thin Shell Pattern).
/// </summary>
internal static class PublisherOperations
{
    internal const string ExchangeName = "referenceservice.comparison";
    internal const string RoutingKey = "instrument.status.changed";

    internal static readonly CachedString CachedExchangeName = new(ExchangeName);
    internal static readonly CachedString CachedRoutingKey = new(RoutingKey);

    public static async Task ConnectAsync(
        PublisherContext ctx,
        string connectionString,
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

            // Mask connection string inline (G3 — single-use, no wrapper function)
            var uri = new Uri(connectionString);
            var userInfo = !string.IsNullOrEmpty(uri.UserInfo) ? "***:***" : "";
            logger.LogInformation(
                "Connecting to RabbitMQ at {ConnectionString}",
                $"{uri.Scheme}://{userInfo}@{uri.Host}:{uri.Port}");

            // Build connection + channel as locals; only store atomically on success
            var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
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
}
