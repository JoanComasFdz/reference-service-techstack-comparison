using System.Net.Http.Json;
using System.Text;
using JoanComasFdz.Result;
using Microsoft.Extensions.Logging;
using static JoanComasFdz.Result.Result<JoanComasFdz.Result.Unit, string>;

namespace PerformanceTester.Infrastructure.RabbitMQ;

/// <summary>
/// Service for clearing RabbitMQ queues during testing.
/// Uses RabbitMQ Management HTTP API to discover and purge queues dynamically.
/// Mimics the behavior of the clear-rabbitmq.sh script.
/// </summary>
internal sealed class RabbitMqCleaner
{
    private static readonly HttpClient SharedHttpClient = new();
    private readonly System.Net.Http.Headers.AuthenticationHeaderValue _authHeader;
    private readonly ILogger<RabbitMqCleaner> _logger;
    private readonly string _managementUrl;
    private readonly string _vhost;

    /// <summary>
    /// Creates a new RabbitMQ cleaner instance.
    /// </summary>
    /// <param name="connectionString">AMQP connection string (format: amqp://user:pass@host:port/vhost)</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="managementPort">Optional Management API port. If not specified, infers from AMQP port:
    /// - Standard RabbitMQ (port 5672) → Management API on 15672
    /// - Testcontainers (port 20001) → Management API on 20002</param>
    public RabbitMqCleaner(string connectionString, ILogger<RabbitMqCleaner> logger, int? managementPort = null)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        // Parse connection string to extract host, port, credentials
        // Format: amqp://user:pass@host:port/vhost
        var uri = new Uri(connectionString);
        var host = uri.Host;
        var username = uri.UserInfo.Split(':')[0];
        var password = uri.UserInfo.Split(':')[1];
        _vhost = string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/"
            ? "/"
            : uri.AbsolutePath.TrimStart('/');

        // Determine Management API port
        int resolvedManagementPort;

        if (managementPort.HasValue)
        {
            // Explicitly provided - use it
            resolvedManagementPort = managementPort.Value;
        }
        else
        {
            // Infer from AMQP port
            var amqpPort = uri.Port;

            if (amqpPort == 5672)
            {
                // Standard RabbitMQ installation
                resolvedManagementPort = 15672;
            }
            else if (amqpPort >= 20000 && amqpPort < 30000)
            {
                // Testcontainer pattern: AMQP port 20001 -> Management port 20002
                resolvedManagementPort = amqpPort + 1;
            }
            else
            {
                // Unknown configuration, try standard offset
                resolvedManagementPort = 15672;
            }
        }

        _managementUrl = $"http://{host}:{resolvedManagementPort}/api";

        // Store auth header for per-request use (static HttpClient is shared)
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        _authHeader = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);
    }

    /// <summary>
    /// Purges all messages from all queues in the default vhost.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success with Unit, or a Failure describing what went wrong.</returns>
    public async Task<Result<Unit, string>> ClearAllQueuesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=========================================");
        _logger.LogInformation("Clearing RabbitMQ Queues");
        _logger.LogInformation("=========================================");

        try
        {
            // Get list of all queues from Management API
            var queues = await GetAllQueuesAsync(cancellationToken);

            if (queues.Count == 0)
            {
                _logger.LogInformation("No queues found or unable to list queues");
                return new Success(Unit.Value);
            }

            _logger.LogInformation("Found {QueueCount} queues: {QueueNames}",
                queues.Count,
                string.Join(", ", queues));

            // Purge each queue
            int successCount = 0;
            int failureCount = 0;

            foreach (var queueName in queues)
            {
                _logger.LogInformation("Purging queue: {QueueName}", queueName);

                try
                {
                    await PurgeQueueAsync(queueName, cancellationToken);
                    _logger.LogInformation("Successfully purged queue: {QueueName}", queueName);
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to purge queue: {QueueName}", queueName);
                    failureCount++;
                }
            }

            _logger.LogInformation("========================================");
            _logger.LogInformation("RabbitMQ queues cleared successfully");
            _logger.LogInformation("Purged {SuccessCount} out of {TotalCount} queues", successCount, queues.Count);
            _logger.LogInformation("========================================");

            if (failureCount > 0)
            {
                return new Failure($"Failed to purge {failureCount} out of {queues.Count} queues");
            }

            return new Success(Unit.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear RabbitMQ queues");
            return new Failure($"Failed to clear RabbitMQ queues: {ex.Message}");
        }
    }

    private async Task<List<string>> GetAllQueuesAsync(CancellationToken cancellationToken)
    {
        try
        {
            // URL encode the vhost (/ becomes %2F)
            var encodedVhost = Uri.EscapeDataString(_vhost);
            var url = $"{_managementUrl}/queues/{encodedVhost}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = _authHeader;
            var response = await SharedHttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var queues = await response.Content.ReadFromJsonAsync<List<QueueInfo>>(cancellationToken);

            return queues?.Select(q => q.Name).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve queue list from Management API");
            return [];
        }
    }

    private async Task PurgeQueueAsync(string queueName, CancellationToken cancellationToken)
    {
        // URL encode the vhost and queue name
        var encodedVhost = Uri.EscapeDataString(_vhost);
        var encodedQueueName = Uri.EscapeDataString(queueName);
        var url = $"{_managementUrl}/queues/{encodedVhost}/{encodedQueueName}/contents";

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = _authHeader;
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
