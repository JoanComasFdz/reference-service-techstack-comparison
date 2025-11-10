using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.IntegrationTesting.Logging;
using SystemBase = PerformanceTester.IntegrationTesting.System;

namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// System under test for Docker monitoring integration tests.
/// Extends base System with DockerMonitoring-specific components.
/// </summary>
public sealed class DockerMonitoringSystem : SystemBase
{
    private IHost? _host;

    public IDockerMonitor PostgresMonitor { get; private set; } = null!;
    public IDockerMonitor RabbitMqMonitor { get; private set; } = null!;

    /// <summary>
    /// Initializes Docker monitoring services for PostgreSQL and RabbitMQ containers.
    /// </summary>
    protected override void InitializeSystem()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        if (base.Output != null)
        {
            builder.Logging.AddXunitOutput(base.Output);
        }

        builder.Services.AddDockerMonitoring(
            containerName: "performance-tester-postgres",
            samplingInterval: TimeSpan.FromMilliseconds(500));

        builder.Services.AddDockerMonitoring(
            containerName: "performance-tester-rabbitmq",
            samplingInterval: TimeSpan.FromMilliseconds(500));

        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
        });

        _host = builder.Build();

        // Resolve monitors individually by container name
        var monitors = _host.Services.GetServices<IDockerMonitor>().ToList();
        PostgresMonitor = monitors.First(m => m.ContainerName == "performance-tester-postgres");
        RabbitMqMonitor = monitors.First(m => m.ContainerName == "performance-tester-rabbitmq");
    }

    /// <summary>
    /// Starts all BackgroundServices (begins monitoring).
    /// </summary>
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_host == null)
            throw new InvalidOperationException("System not initialized");

        await _host.StartAsync(cancellationToken);
    }

    /// <summary>
    /// Stops all BackgroundServices (ends monitoring).
    /// Safe to call multiple times (idempotent).
    /// </summary>
    public async Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_host == null)
            return; // Already stopped or never started

        await _host.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Disposes host and all services.
    /// Defensively stops monitoring if still running (tests may forget to call StopMonitoringAsync).
    /// </summary>
    public override void Dispose()
    {
        if (_host != null)
        {
            try
            {
                // Defensive cleanup: stop monitoring if still running
                // Synchronous wait is acceptable in Dispose() context
                StopMonitoringAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore errors (already stopped or never started)
            }

            _host.Dispose();
            _host = null;
        }

        base.Dispose();
    }
}
