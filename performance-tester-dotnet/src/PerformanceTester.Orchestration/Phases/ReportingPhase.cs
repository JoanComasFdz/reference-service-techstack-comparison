using JoanComasFdz.Result;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.Reporting;
using PerformanceTester.Reporting.ReportGeneration;
using PerformanceTester.Reporting.ValueObjects;
using PerformanceTester.SystemMonitoring;
using Serilog.Context;
using static JoanComasFdz.Result.Result<PerformanceTester.Reporting.TestReport, string>;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Phase 3: Collect metrics, build report, generate JSON and chart.
/// Expects monitors to be already stopped by the orchestrator.
/// </summary>
internal static class ReportingPhase
{
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

    public static async Task<Result<TestReport, string>> ExecuteAsync(
        TestResult testResult,
        TestConfiguration config,
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

        try
        {
            logger.LogInformation("Starting reporting phase");

            // Step 1: Collect all metrics from monitors
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

            return new Success(testReport);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Reporting phase failed: {Message}", ex.Message);
            return new Failure(ex.Message);
        }
    }
}
