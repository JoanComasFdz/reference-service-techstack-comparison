using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.Database;
using PerformanceTester.Infrastructure.RabbitMQ;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.IntegrationTesting.Logging;
using Xunit.Abstractions;

namespace PerformanceTester.Infrastructure.IntegrationTests.Infrastructure;

public sealed class Infrastructure
{
    private IHost? _host;

    /// <summary>
    /// Service discovery delegate for finding processes on ports.
    /// </summary>
    public FindServiceProcessIdDelegate FindServiceProcessId { get; private set; } = null!;

    /// <summary>
    /// Database management for clearing test data.
    /// </summary>
    public ClearDatabaseDelegate ClearDatabase { get; private set; } = null!;

    /// <summary>
    /// Delegate for clearing all RabbitMQ queues.
    /// </summary>
    public ClearAllQueuesDelegate ClearAllQueues { get; private set; } = null!;

    public Infrastructure(
        string postgreSQLConnectionString,
        string rabbitMQConnectionString,
        Port? rabbitMQManagementPort,
        ITestOutputHelper? output = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        // Wire up xUnit test output if provided
        if (output != null)
        {
            builder.Logging.AddXunitOutput(output);
            builder.Logging.SetMinimumLevel(LogLevel.Debug); // Show all logs in tests
        }

        builder.Services.AddInfrastructure(
            postgreSQLConnectionString,
            rabbitMQConnectionString,
            rabbitMQManagementPort);

        _host = builder.Build();

        FindServiceProcessId = _host.Services.GetRequiredService<FindServiceProcessIdDelegate>();
        ClearDatabase = _host.Services.GetRequiredService<ClearDatabaseDelegate>();
        ClearAllQueues = _host.Services.GetRequiredService<ClearAllQueuesDelegate>();
    }

    public void Dispose()
    {
        _host?.Dispose();
    }
}
