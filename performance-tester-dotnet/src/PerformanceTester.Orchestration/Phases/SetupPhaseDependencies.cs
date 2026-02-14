using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Builds the <see cref="RunSetup"/> delegate from DI-resolved interfaces.
/// </summary>
internal static class SetupPhaseDependencies
{
    public static RunSetup Build(
        IServiceProvider services,
        PhasesToolbox.ClearDatabase clearDatabase,
        PhasesToolbox.ClearAllQueues clearAllQueues,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var serviceDiscovery = services.GetRequiredService<IServiceDiscovery>();
        var hostLifetime = services.GetRequiredService<IHostApplicationLifetime>();
        var host = services.GetRequiredService<IHost>();
        var dockerMonitors = services.GetRequiredService<IEnumerable<IDockerMonitor>>();
        var eventPublisher = services.GetRequiredService<IEventPublisher>();

        return (testRunId) => SetupPhase.ExecuteAsync(
            testRunId,
            findServiceProcessId: () => serviceDiscovery.FindServiceProcessIdAsync(config.ServicePort.Value, TimeSpan.FromSeconds(30), ct),
            isMonitoringStarted: () => hostLifetime.ApplicationStarted.IsCancellationRequested,
            startMonitoring: () => host.StartAsync(ct),
            warmupDockerApi: () => Task.WhenAll(dockerMonitors.Select(m => m.WarmupAsync(ct))),
            clearDatabase: clearDatabase,
            clearAllQueues: clearAllQueues,
            connectEventPublisher: () => eventPublisher.ConnectAsync(ct),
            logger);
    }
}
