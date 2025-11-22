using System.Net.Http.Headers;
using System.Text;
using RabbitMQ.Client;

namespace PerformanceTester.IntegrationTesting;

/// <summary>
/// Represents the RabbitMQ message broker part of the system.
/// </summary>
/// <param name="ConnectionString">AMQP connection string (e.g., amqp://user:pass@host:port)</param>
/// <param name="ManagementPort">Management API port (default: 15672 for standard, 20002 for testcontainers)</param>
public class RabbitMQ(string ConnectionString, int? ManagementPort = null) : IDisposable
{
    private readonly Lazy<HttpClient> _httpClient = new(CreateHttpClient);
    
    private bool _disposed;
    /// <summary>
    /// The connection string used to connect to the RabbitMQ instance via AMQP.
    /// <para>
    /// Use it for your system under test configuration so that it can connect to RabbitMQ.
    /// </para>
    /// </summary>
    public string ConnectionString { get; } = ConnectionString;

    /// <summary>
    /// The HTTP Management API port.
    /// If not specified, it will be inferred from the AMQP port:
    /// - Standard RabbitMQ (port 5672) -> Management API on 15672
    /// - Testcontainers (port 20001) -> Management API on 20002
    /// </summary>
    public int? ManagementPort { get; } = ManagementPort;

    /// <summary>
    /// Creates a queue and publishes the specified number of test messages to it.
    /// </summary>
    /// <param name="queueName">The name of the queue to create</param>
    /// <param name="messageCount">The number of test messages to publish</param>
    public async Task CreateQueueWithMessagesAsync(string queueName, int messageCount)
    {
        var factory = new ConnectionFactory { Uri = new Uri(ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false);

        for (int i = 0; i < messageCount; i++)
        {
            var body = Encoding.UTF8.GetBytes($"Test message {i}");
            await channel.BasicPublishAsync(exchange: "", routingKey: queueName, body: body);
        }
    }

    /// <summary>
    /// Gets the current message count for the specified queue.
    /// </summary>
    /// <param name="queueName">The name of the queue to query</param>
    /// <returns>The number of messages currently in the queue</returns>
    public async Task<uint> GetQueueMessageCountAsync(string queueName)
    {
        var factory = new ConnectionFactory { Uri = new Uri(ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        var queueInfo = await channel.QueueDeclarePassiveAsync(queueName);
        return queueInfo.MessageCount;
    }

    /// <summary>
    /// Deletes the specified queue. Silently ignores errors (useful for cleanup operations).
    /// </summary>
    /// <param name="queueName">The name of the queue to delete</param>
    public async Task DeleteQueueAsync(string queueName)
    {
        try
        {
            var factory = new ConnectionFactory { Uri = new Uri(ConnectionString) };
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();

            await channel.QueueDeleteAsync(queueName);
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }

    /// <summary>
    /// Purges all messages from the specified queue.
    /// Silently ignores errors (useful for cleanup operations).
    /// </summary>
    /// <param name="queueName">The name of the queue to purge</param>
    public async Task PurgeQueueAsync(string queueName)
    {
        try
        {
            var factory = new ConnectionFactory { Uri = new Uri(ConnectionString) };
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();

            await channel.QueuePurgeAsync(queueName);
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }

    /// <summary>
    /// Declares an exchange with the specified configuration.
    /// Idempotent - safe to call multiple times.
    /// </summary>
    /// <param name="exchangeName">Name of the exchange</param>
    /// <param name="exchangeType">Type of exchange (topic, direct, fanout, headers)</param>
    /// <param name="durable">Whether the exchange survives broker restart</param>
    public async Task DeclareExchangeAsync(string exchangeName, string exchangeType = "topic", bool durable = true)
    {
        var factory = new ConnectionFactory { Uri = new Uri(ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: exchangeType,
            durable: durable,
            autoDelete: false,
            arguments: null);
    }

    /// <summary>
    /// Declares a queue and binds it to an exchange with the specified routing key.
    /// </summary>
    /// <param name="queueName">Name of the queue to declare</param>
    /// <param name="exchangeName">Name of the exchange to bind to</param>
    /// <param name="routingKey">Routing key for binding</param>
    /// <param name="durable">Whether the queue survives broker restart</param>
    public async Task DeclareAndBindQueueAsync(
        string queueName,
        string exchangeName,
        string routingKey,
        bool durable = true)
    {
        var factory = new ConnectionFactory { Uri = new Uri(ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        // Declare queue
        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: durable,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        // Bind queue to exchange
        await channel.QueueBindAsync(
            queue: queueName,
            exchange: exchangeName,
            routingKey: routingKey,
            arguments: null);
    }

    /// <summary>
    /// Closes all open RabbitMQ connections via the Management API.
    /// This is useful for cleaning up lingering connections between tests.
    /// Silently ignores errors (useful for cleanup operations).
    /// </summary>
    public async Task CloseAllConnectionsAsync()
    {
        try
        {
            // Infer management port if not specified
            var managementPort = ManagementPort;
            if (!managementPort.HasValue)
            {
                var uri = new Uri(ConnectionString);
                managementPort = uri.Port switch
                {
                    5672 => 15672,   // Standard RabbitMQ
                    20001 => 20002,  // Testcontainers
                    _ => 15672       // Default
                };
            }

            var baseUrl = $"http://localhost:{managementPort}";

            // Get all connections
            var connectionsUrl = $"{baseUrl}/api/connections";
            var response = await _httpClient.Value.GetAsync(connectionsUrl);

            if (!response.IsSuccessStatusCode)
            {
                // Silently ignore failures
                return;
            }

            var connectionsJson = await response.Content.ReadAsStringAsync();
            var connections = global::System.Text.Json.JsonDocument.Parse(connectionsJson);

            // Close each connection
            foreach (var connection in connections.RootElement.EnumerateArray())
            {
                if (connection.TryGetProperty("name", out var nameProperty))
                {
                    var connectionName = nameProperty.GetString();
                    if (!string.IsNullOrEmpty(connectionName))
                    {
                        // URL encode the connection name
                        var encodedName = Uri.EscapeDataString(connectionName);
                        var deleteUrl = $"{baseUrl}/api/connections/{encodedName}";

                        // Send DELETE request (ignore response)
                        await _httpClient.Value.DeleteAsync(deleteUrl);
                    }
                }
            }
        }
        catch
        {
            // Silently ignore all errors during cleanup
        }
    }

    /// <summary>
    /// Creates and configures an HttpClient for RabbitMQ Management API.
    /// </summary>
    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:admin"));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", authToken);
        return client;
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_httpClient.IsValueCreated)
        {
            _httpClient.Value.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}