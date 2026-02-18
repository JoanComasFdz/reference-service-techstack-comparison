using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring.ValueObjects;
using PerformanceTester.IntegrationTesting.Logging;
using SystemBase = PerformanceTester.IntegrationTesting.System;

namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// System under test for Docker monitoring integration tests.
/// Exposes named delegates (not IDockerMonitor instances) per FP refactoring design.
/// </summary>
public sealed class DockerMonitoringSystem : SystemBase
{
    private IHost? _host;

    public static readonly PostgresContainerName PostgresName = PostgresContainerName.FromString("performance-tester-postgres");
    public static readonly RabbitMqContainerName RabbitMqName = RabbitMqContainerName.FromString("performance-tester-rabbitmq");

    public WarmupDockerMonitors WarmupDockerMonitors { get; private set; } = null!;
    public StartDockerMonitoring StartDockerMonitoring { get; private set; } = null!;
    public GetDockerMetrics GetDockerMetrics { get; private set; } = null!;

    /// <summary>
    /// Initializes Docker monitoring services for PostgreSQL and RabbitMQ containers.
    /// </summary>
    protected override async Task InitializeSystemAsync()
    {
        await base.InitializeSystemAsync();

        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        if (base.Output != null)
        {
            builder.Logging.AddXunitOutput(base.Output);
        }

        builder.Services.AddDockerMonitoring(RabbitMqName, PostgresName);

        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
        });

        _host = builder.Build();

        WarmupDockerMonitors = _host.Services.GetRequiredService<WarmupDockerMonitors>();
        StartDockerMonitoring = _host.Services.GetRequiredService<StartDockerMonitoring>();
        GetDockerMetrics = _host.Services.GetRequiredService<GetDockerMetrics>();
    }

    /// <summary>
    /// Starts all BackgroundServices and begins monitoring.
    /// </summary>
    public async Task StartMonitoringAsync(
        ReportDockerMonitorProgress? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (_host == null)
        {
            throw new InvalidOperationException("System not initialized");
        }

        await _host.StartAsync(cancellationToken);
        await StartDockerMonitoring(reportProgress ?? (_ => { }), cancellationToken);
    }

    /// <summary>
    /// Stops all BackgroundServices (ends monitoring).
    /// </summary>
    public async Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_host == null)
        {
            return;
        }

        await _host.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Disposes host and all services.
    /// </summary>
    public override void Dispose()
    {
        if (_host != null)
        {
            try
            {
                StopMonitoringAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore errors
            }

            _host.Dispose();
            _host = null;
        }

        base.Dispose();
    }
}
