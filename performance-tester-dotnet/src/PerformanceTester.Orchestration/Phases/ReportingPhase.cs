using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.Reporting;
using PerformanceTester.Reporting.ReportGeneration;
using PerformanceTester.Reporting.ValueObjects;
using PerformanceTester.SystemMonitoring;
using Serilog.Context;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Phase 3: Stop monitors, collect metrics, build report, generate JSON and chart.
/// </summary>
internal static class ReportingPhase
{
    /// <summary>
    /// Disconnects from the RabbitMQ event publisher.
    /// </summary>
    public delegate Task DisconnectEventPublisher();

    /// <summary>
    /// Stops all monitoring services (BackgroundServices via IHost).
    /// </summary>
    public delegate Task StopMonitoring();

    /// <summary>
    /// Returns throughput samples collected during the event test.
    /// </summary>
    public delegate IReadOnlyCollection<EventThroughputSample> GetThroughputSamples();

    /// <summary>
    /// Returns process resource metrics (CPU, memory, threads) collected during the test.
    /// </summary>
    public delegate IReadOnlyCollection<ProcessMetrics> GetProcessMetrics();

    /// <summary>
    /// Returns system-wide metrics (CPU, memory) collected during the test.
    /// </summary>
    public delegate IReadOnlyCollection<SystemMetrics> GetSystemMetrics();

    /// <summary>
    /// Returns Docker container metrics for the RabbitMQ container.
    /// </summary>
    public delegate IReadOnlyCollection<DockerMetrics> GetRabbitMqMetrics();

    /// <summary>
    /// Returns Docker container metrics for the PostgreSQL container.
    /// </summary>
    public delegate IReadOnlyCollection<DockerMetrics> GetPostgresMetrics();

    /// <summary>
    /// Detects and returns system hardware/OS information.
    /// </summary>
    public delegate Task<SystemInfo?> GetSystemInfo();

    /// <summary>
    /// Generates JSON report files to the specified output folder.
    /// </summary>
    public delegate Task GenerateReport(ResultsOutputFolder outputFolder, TestReport testReport);

    /// <summary>
    /// Generates a PNG chart to the specified output folder.
    /// Returns the full path to the generated chart file.
    /// </summary>
    public delegate Task<string> GenerateChart(ResultsOutputFolder outputFolder, TestReport testReport, ILogger logger);

    public static async Task<TestReport> ExecuteAsync(
        TestResult testResult,
        TestConfiguration config,
        DisconnectEventPublisher disconnectEventPublisher,
        StopMonitoring stopMonitoring,
        GetThroughputSamples getThroughputSamples,
        GetProcessMetrics getProcessMetrics,
        GetSystemMetrics getSystemMetrics,
        GetRabbitMqMetrics getRabbitMqMetrics,
        GetPostgresMetrics getPostgresMetrics,
        GetSystemInfo getSystemInfo,
        GenerateReport generateReport,
        GenerateChart generateChart,
        ILogger logger)
    {
        using var _ = LogContext.PushProperty("Phase", "Reporting");

        logger.LogInformation("Starting reporting phase");

        // Step 1: Disconnect from RabbitMQ event publisher
        logger.LogInformation("Disconnecting from RabbitMQ event publisher...");
        await disconnectEventPublisher();
        logger.LogInformation("RabbitMQ event publisher disconnected");

        // Step 2: Stop IHost (all BackgroundServices stop)
        logger.LogInformation("Stopping monitoring services...");
        await stopMonitoring();
        logger.LogInformation("All monitoring services stopped");

        // Step 3: Collect all metrics from monitors
        logger.LogInformation("Collecting metrics from monitors...");

        var throughputSamples = getThroughputSamples();
        var processMetrics = getProcessMetrics();
        var systemMetrics = getSystemMetrics();
        var rabbitMqMetrics = getRabbitMqMetrics();
        var postgresMetrics = getPostgresMetrics();

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
        var systemInfo = await getSystemInfo();

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

        await generateReport(config.ResultsFolder, testReport);

        logger.LogInformation("JSON reports generated");

        // Step 7: Generate chart
        logger.LogInformation("Generating chart to {Folder}", config.ResultsFolder);

        var chartPath = await generateChart(config.ResultsFolder, testReport, logger);

        logger.LogInformation("Metrics chart saved to: {Path}", chartPath);

        logger.LogInformation("Chart generated");

        logger.LogInformation("Reporting phase complete");

        return testReport;
    }
}
