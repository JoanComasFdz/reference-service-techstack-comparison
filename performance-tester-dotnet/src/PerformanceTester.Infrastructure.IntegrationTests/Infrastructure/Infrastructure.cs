using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.RabbitMQ;
using PerformanceTester.IntegrationTesting.Logging;
using Xunit.Abstractions;

namespace PerformanceTester.Infrastructure.IntegrationTests.Infrastructure;

public sealed class Infrastructure
{
    private IHost? _host;

    /// <summary>
    /// Service discovery delegate for finding processes on ports.
    /// </summary>
    public FindServiceProcessId FindServiceProcessId { get; private set; } = null!;

    /// <summary>
    /// Database management for clearing test data.
    /// </summary>
    public IDatabase Database { get; private set; } = null!;

    /// <summary>
    /// Delegate for clearing all RabbitMQ queues.
    /// </summary>
    public ClearAllQueues ClearAllQueues { get; private set; } = null!;

    public Infrastructure(
        string postgreSQLConnectionString,
        string rabbitMQConnectionString,
        int? rabbitMQManagementPort,
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

        FindServiceProcessId = _host.Services.GetRequiredService<FindServiceProcessId>();
        Database = _host.Services.GetRequiredService<IDatabase>();
        ClearAllQueues = _host.Services.GetRequiredService<ClearAllQueues>();
    }

    public void Dispose()
    {
        _host?.Dispose();
    }
}
