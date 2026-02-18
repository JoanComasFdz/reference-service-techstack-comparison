using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.Database;
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
    public ClearDatabase ClearDatabase { get; private set; } = null!;

    /// <summary>
    /// RabbitMQ management for clearing queues.
    /// </summary>
    public IRabbitMQ RabbitMQ { get; private set; } = null!;

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
        ClearDatabase = _host.Services.GetRequiredService<ClearDatabase>();
        RabbitMQ = _host.Services.GetRequiredService<IRabbitMQ>();
    }

    public void Dispose()
    {
        _host?.Dispose();
    }
}
