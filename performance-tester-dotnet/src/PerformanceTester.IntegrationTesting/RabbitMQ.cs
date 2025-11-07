namespace PerformanceTester.IntegrationTesting;

/// <summary>
/// Represents the RabbitMQ message broker part of the system.
/// </summary>
/// <param name="ConnectionString">AMQP connection string (e.g., amqp://user:pass@host:port)</param>
/// <param name="ManagementPort">Management API port (default: 15672 for standard, 20002 for testcontainers)</param>
public class RabbitMQ(string ConnectionString, int? ManagementPort = null)
{
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
}