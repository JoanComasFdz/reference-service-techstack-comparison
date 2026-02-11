using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.EventPublishing;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.Reporting;
using PerformanceTester.Reporting.ChartGeneration;
using PerformanceTester.Reporting.ReportGeneration;
using PerformanceTester.SystemMonitoring;
using Serilog.Context;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Phase 3: Stop monitors, collect metrics, build report, generate JSON and chart.
/// </summary>
internal static class ReportingPhase
{
    public static async Task<TestReport> ExecuteAsync(
        TestResult testResult,
        TestConfiguration config,
        IEventPublisher eventPublisher,
        IHost host,
        IMetricsCollector metricsCollector,
        IProcessMonitor processMonitor,
        ISystemMonitor systemMonitor,
        IEnumerable<IDockerMonitor> dockerMonitors,
        ISystemInfoDetector systemInfoDetector,
        ReportGenerator reportGenerator,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var _ = LogContext.PushProperty("Phase", "Reporting");

        logger.LogInformation("Starting reporting phase");

        // Step 1: Disconnect from RabbitMQ event publisher
        logger.LogInformation("Disconnecting from RabbitMQ event publisher...");
        await eventPublisher.DisconnectAsync(cancellationToken);
        logger.LogInformation("RabbitMQ event publisher disconnected");

        // Step 2: Stop IHost (all BackgroundServices stop)
        logger.LogInformation("Stopping monitoring services...");
        await host.StopAsync(cancellationToken);
        logger.LogInformation("All monitoring services stopped");

        // Step 3: Collect all metrics from monitors
        logger.LogInformation("Collecting metrics from monitors...");

        var throughputSamples = metricsCollector.GetThroughputSamples();
        var processMetrics = processMonitor.GetCollectedMetrics();
        var systemMetrics = systemMonitor.GetCollectedMetrics();

        // Get Docker monitors by container name
        var rabbitMqMonitor = dockerMonitors.Single(m => m.ContainerName == config.RabbitMqContainerName.Value);
        var postgresMonitor = dockerMonitors.Single(m => m.ContainerName == config.PostgresContainerName.Value);

        var rabbitMqMetrics = rabbitMqMonitor?.GetCollectedMetrics() ?? [];
        var postgresMetrics = postgresMonitor?.GetCollectedMetrics() ?? [];

        logger.LogInformation(
            "Metrics collected: {Throughput} throughput samples, " +
            "{Process} process samples, {System} system samples, " +
            "{RabbitMQ} RabbitMQ samples, {Postgres} PostgreSQL samples",
            throughputSamples.Count,
            processMetrics.Count,
            systemMetrics.Count,
            rabbitMqMetrics.Count,
            postgresMetrics.Count);

        // Step 4: Get system information (cached)
        var systemInfo = await systemInfoDetector.GetSystemInfoAsync(cancellationToken);

        // Step 5: Build TestReport
        logger.LogInformation("Building test report...");

        var testReport = TestReportBuilder.Build(
            testResult,
            config,
            throughputSamples,
            processMetrics,
            systemMetrics,
            rabbitMqMetrics,
            postgresMetrics,
            systemInfo);

        // Step 6: Generate JSON reports
        logger.LogInformation("Generating JSON reports to {Folder}", config.ResultsFolder);

        await reportGenerator.GenerateReportAsync(
            config.ResultsFolder,
            testReport,
            cancellationToken);

        logger.LogInformation("JSON reports generated");

        // Step 7: Generate chart
        logger.LogInformation("Generating chart to {Folder}", config.ResultsFolder);

        var chartPath = await ChartGenerator.GenerateChartAsync(
            config.ResultsFolder,
            testReport,
            logger,
            cancellationToken: cancellationToken);

        logger.LogInformation("Metrics chart saved to: {Path}", chartPath);

        logger.LogInformation("Chart generated");

        logger.LogInformation("Reporting phase complete");

        return testReport;
    }
}
