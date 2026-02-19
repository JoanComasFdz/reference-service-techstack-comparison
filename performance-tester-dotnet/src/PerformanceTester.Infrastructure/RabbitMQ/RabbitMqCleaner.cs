using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using PerformanceTester.Functional;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;
using static PerformanceTester.Functional.Result<PerformanceTester.Functional.Unit, string>;

namespace PerformanceTester.Infrastructure.RabbitMQ;

/// <summary>
/// Pure functions for clearing RabbitMQ queues during performance testing.
/// Uses RabbitMQ Management HTTP API to discover and purge queues dynamically.
/// Static class with explicit parameters (Guidelines 1, 2).
/// </summary>
internal static class RabbitMqCleaner
{
    private static readonly HttpClient SharedHttpClient = new();

    /// <summary>
    /// Pre-parsed RabbitMQ Management API connection parameters.
    /// Created per-call from the raw connection string (cheap URI parse).
    /// </summary>
    private sealed record ManagementApiConfig(
        string ManagementUrl,
        AuthenticationHeaderValue AuthHeader,
        string Vhost);

    /// <summary>
    /// Purges all messages from all queues in the default vhost.
    /// </summary>
    /// <param name="connectionString">AMQP connection string (format: amqp://user:pass@host:port/vhost)</param>
    /// <param name="managementPort">Optional Management API port. If not specified, infers from AMQP port:
    /// standard (5672) → 15672, Testcontainers (20000+) → AMQP port + 1.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success with Unit, or a Failure describing what went wrong.</returns>
    public static async Task<Result<Unit, string>> ClearAllQueuesAsync(
        string connectionString,
        Port? managementPort,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var api = ParseManagementApiConfig(connectionString, managementPort);

        logger.LogInformation("=========================================");
        logger.LogInformation("Clearing RabbitMQ Queues");
        logger.LogInformation("=========================================");

        try
        {
            // Get list of all queues from Management API
            var queues = await GetAllQueuesAsync(api, logger, cancellationToken);

            if (queues.Count == 0)
            {
                logger.LogInformation("No queues found or unable to list queues");
                return new Success(Unit.Value);
            }

            logger.LogInformation("Found {QueueCount} queues: {QueueNames}",
                queues.Count,
                string.Join(", ", queues));

            // Purge each queue sequentially, collecting per-queue outcomes
            var purgeResults = await Task.WhenAll(
                queues.Select(q => TryPurgeQueueAsync(api, q, logger, cancellationToken))
                );

            var failureCount = purgeResults.Count(succeeded => !succeeded);

            logger.LogInformation("========================================");
            logger.LogInformation("Purged {SuccessCount} out of {TotalCount} queues",
                queues.Count - failureCount, queues.Count);
            logger.LogInformation("========================================");

            return failureCount > 0
                ? new Failure($"Failed to purge {failureCount} out of {queues.Count} queues")
                : new Success(Unit.Value);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clear RabbitMQ queues");
            return new Failure($"Failed to clear RabbitMQ queues: {ex.Message}");
        }
    }

    private static ManagementApiConfig ParseManagementApiConfig(
        string connectionString,
        Port? managementPort)
    {
        var uri = new Uri(connectionString);
        var host = uri.Host;
        var username = uri.UserInfo.Split(':')[0];
        var password = uri.UserInfo.Split(':')[1];

        var vhost = string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/"
            ? "/"
            : uri.AbsolutePath.TrimStart('/');

        // Determine Management API port
        int resolvedPort;

        if (managementPort is { } port)
        {
            // Explicitly provided — unwrap value object at infrastructure boundary (Guideline 20)
            resolvedPort = port.Value;
        }
        else
        {
            // Infer from AMQP port
            var amqpPort = uri.Port;

            if (amqpPort == 5672)
            {
                // Standard RabbitMQ installation
                resolvedPort = 15672;
            }
            else if (amqpPort >= 20000 && amqpPort < 30000)
            {
                // Testcontainer pattern: AMQP port 20001 -> Management port 20002
                resolvedPort = amqpPort + 1;
            }
            else
            {
                // Unknown configuration, try standard offset
                resolvedPort = 15672;
            }
        }

        var managementUrl = $"http://{host}:{resolvedPort}/api";

        // Compute auth header for per-request use (static HttpClient is shared)
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        var authHeader = new AuthenticationHeaderValue("Basic", authToken);

        return new ManagementApiConfig(managementUrl, authHeader, vhost);
    }

    private static async Task<List<string>> GetAllQueuesAsync(
        ManagementApiConfig api,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // URL encode the vhost (/ becomes %2F)
            var encodedVhost = Uri.EscapeDataString(api.Vhost);
            var url = $"{api.ManagementUrl}/queues/{encodedVhost}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = api.AuthHeader;
            var response = await SharedHttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var queues = await response.Content.ReadFromJsonAsync<List<QueueInfo>>(cancellationToken);

            return queues?.Select(q => q.Name).ToList() ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve queue list from Management API");
            return [];
        }
    }

    private static async Task<bool> TryPurgeQueueAsync(
        ManagementApiConfig api,
        string queueName,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Purging queue: {QueueName}", queueName);
        try
        {
            await PurgeQueueAsync(api, queueName, cancellationToken);
            logger.LogInformation("Successfully purged queue: {QueueName}", queueName);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to purge queue: {QueueName}", queueName);
            return false;
        }
    }

    private static async Task PurgeQueueAsync(
        ManagementApiConfig api,
        string queueName,
        CancellationToken cancellationToken)
    {
        // URL encode the vhost and queue name
        var encodedVhost = Uri.EscapeDataString(api.Vhost);
        var encodedQueueName = Uri.EscapeDataString(queueName);
        var url = $"{api.ManagementUrl}/queues/{encodedVhost}/{encodedQueueName}/contents";

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = api.AuthHeader;
        var response = await SharedHttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Represents queue information from RabbitMQ Management API.
    /// Only includes the fields we need.
    /// </summary>
    private sealed class QueueInfo
    {
        public string Name { get; set; } = string.Empty;
    }
}
