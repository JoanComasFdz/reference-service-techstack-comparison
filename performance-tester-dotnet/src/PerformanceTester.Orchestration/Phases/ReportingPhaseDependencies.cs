using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.Reporting;
using PerformanceTester.Reporting.ChartGeneration;
using PerformanceTester.Reporting.ReportGeneration;
using PerformanceTester.SystemMonitoring;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Builds the <see cref="TestOrchestrator.RunReporting"/> delegate from DI-resolved interfaces.
/// </summary>
internal static class ReportingPhaseDependencies
{
    public static TestOrchestrator.RunReporting Build(
        IServiceProvider services,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var metricsCollector = services.GetRequiredService<IMetricsCollector>();
        var processMonitor = services.GetRequiredService<IProcessMonitor>();
        var systemMonitor = services.GetRequiredService<ISystemMonitor>();
        var dockerMonitors = services.GetRequiredService<IEnumerable<IDockerMonitor>>();
        var systemInfoDetector = services.GetRequiredService<ISystemInfoDetector>();
        var reportGenerator = services.GetRequiredService<ReportGenerator>();

        return (testResult) => ReportingPhase.ExecuteAsync(
            testResult, config,
            getThroughputSamples: metricsCollector.GetThroughputSamples,
            getProcessMetrics: processMonitor.GetCollectedMetrics,
            getSystemMetrics: systemMonitor.GetCollectedMetrics,
            getRabbitMqMetrics: () => dockerMonitors
                .Single(m => m.ContainerName == config.RabbitMqContainerName.Value)
                .GetCollectedMetrics(),
            getPostgresMetrics: () => dockerMonitors
                .Single(m => m.ContainerName == config.PostgresContainerName.Value)
                .GetCollectedMetrics(),
            getSystemInfo: () => systemInfoDetector.GetSystemInfoAsync(ct),
            generateReport: (folder, report) => reportGenerator.GenerateReportAsync(folder, report, ct),
            generateChart: (folder, report, log) => ChartGenerator.GenerateChartAsync(folder, report, log, ct),
            logger);
    }
}
