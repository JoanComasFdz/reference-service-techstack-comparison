using Xunit.Abstractions;

namespace PerformanceTester.IntegrationTesting;

/// <summary>
/// Represents the whole ecosystem where the service under test runs. It serves as a single entry point for integration tests to discover
/// and access each part of the whole system.
/// </summary>
public class System : IDisposable
{
    /// <summary>
    /// Represents the PostgreSQL database part of the system.
    /// </summary>
    public PostgreSQL PostgreSQL { get; private set; } = null!;

    /// <summary>
    /// Represents the RabbitMQ message broker part of the system.
    /// </summary>
    public RabbitMQ RabbitMQ { get; private set; } = null!;

    /// <summary>
    /// Represents the OS process and port management part of the system.
    /// </summary>
    public OS OS { get; private set; } = null!;

    /// <summary>
    /// Test output helper for logging (optional).
    /// </summary>
    protected ITestOutputHelper? Output { get; private set; }

    /// <summary>
    /// Initializes the system with connection details for PostgreSQL and RabbitMQ.
    /// </summary>
    /// <param name="postgresConnectionString">PostgreSQL connection string</param>
    /// <param name="rabbitMqConnectionString">RabbitMQ AMQP connection string</param>
    /// <param name="rabbitMqManagementPort">Optional RabbitMQ Management API port</param>
    /// <param name="output">Optional test output helper for logging</param>
    internal async Task InitializeAsync(string postgresConnectionString, string rabbitMqConnectionString, int? rabbitMqManagementPort = null, ITestOutputHelper? output = null)
    {
        PostgreSQL = new PostgreSQL(postgresConnectionString);
        RabbitMQ = new RabbitMQ(rabbitMqConnectionString, rabbitMqManagementPort);
        OS = new OS();
        Output = output;

        await InitializeSystemAsync();
    }

    protected virtual Task InitializeSystemAsync() => Task.CompletedTask;

    public virtual void Dispose()
    {
        OS?.Dispose();
        RabbitMQ?.Dispose();
    }
}
