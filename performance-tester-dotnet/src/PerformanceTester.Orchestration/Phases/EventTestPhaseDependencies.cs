using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.SystemMonitoring;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Builds the <see cref="TestOrchestrator.RunEventTest"/> delegate from DI-resolved interfaces.
/// </summary>
internal static class EventTestPhaseDependencies
{
    public static TestOrchestrator.RunEventTest Build(
        IServiceProvider services,
        PhasesToolbox.TrackEvents trackEvents,
        PhasesToolbox.PublishEvents publishEvents,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var systemMonitor = services.GetRequiredService<ISystemMonitor>();
        var metricsCollector = services.GetRequiredService<IMetricsCollector>();
        var processMonitor = services.GetRequiredService<IProcessMonitor>();
        var dockerMonitors = services.GetRequiredService<IEnumerable<IDockerMonitor>>();

        return (serviceProcessId, progress) => EventTestPhase.ExecuteAsync(
            config, serviceProcessId,
            systemCpuCount: systemMonitor.CpuCount,
            systemIsWsl2: systemMonitor.IsWsl2,
            clearSamples: metricsCollector.ClearSamples,
            startProcessMonitoring: (pid) => processMonitor.StartMonitoringAsync(pid, cancellationToken: ct),
            startSystemMonitoring: () => systemMonitor.StartMonitoringAsync(cancellationToken: ct),
            startDockerMonitoring: () => Task.WhenAll(dockerMonitors.Select(m => m.StartMonitoringAsync(cancellationToken: ct))),
            trackEvents: trackEvents,
            publishEvents: publishEvents,
            progress, logger);
    }
}
