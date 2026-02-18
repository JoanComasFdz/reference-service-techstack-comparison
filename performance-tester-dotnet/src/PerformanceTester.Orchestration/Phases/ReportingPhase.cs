using JoanComasFdz.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.Reporting;
using PerformanceTester.Reporting.ChartGeneration;
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

    // -- Dependencies record (bundle of what I need) -------------------------------

    public record Dependencies(
        GetThroughputSamples GetThroughputSamples,
        GetProcessMetrics GetProcessMetrics,
        GetSystemMetrics GetSystemMetrics,
        GetRabbitMqMetrics GetRabbitMqMetrics,
        GetPostgresMetrics GetPostgresMetrics,
        GetSystemInfo GetSystemInfo,
        GenerateReport GenerateReport,
        GenerateChart GenerateChart);

    // -- Factory (how to build what I need from DI) --------------------------------

    public static Dependencies BuildDependencies(
        IServiceProvider services,
        TestConfiguration config,
        CancellationToken ct)
    {
        var metricsCollector = services.GetRequiredService<IMetricsCollector>();
        var processMonitor = services.GetRequiredService<IProcessMonitor>();
        var systemMonitor = services.GetRequiredService<ISystemMonitor>();
        var getDockerMetrics = services.GetRequiredService<GetDockerMetrics>();
        var systemInfoDetector = services.GetRequiredService<ISystemInfoDetector>();
        var reportGenerator = services.GetRequiredService<ReportGenerator>();

        return new Dependencies(
            GetThroughputSamples: metricsCollector.GetThroughputSamples,
            GetProcessMetrics: processMonitor.GetCollectedMetrics,
            GetSystemMetrics: systemMonitor.GetCollectedMetrics,
            GetRabbitMqMetrics: () => getDockerMetrics(config.RabbitMqContainerName),
            GetPostgresMetrics: () => getDockerMetrics(config.PostgresContainerName),
            GetSystemInfo: () => systemInfoDetector.GetSystemInfoAsync(ct),
            GenerateReport: (folder, report) => reportGenerator.GenerateReportAsync(folder, report, ct),
            GenerateChart: (folder, report, log) => ChartGenerator.GenerateChartAsync(folder, report, log, ct));
    }

    // -- Execution (what I do with it) ---------------------------------------------

    public static async Task<Result<TestReport, string>> ExecuteAsync(
        TestResult testResult,
        TestConfiguration config,
        Dependencies deps,
        ILogger logger)
    {
        using var _ = LogContext.PushProperty("Phase", "Reporting");

        try
        {
            logger.LogInformation("Starting reporting phase");

            // Step 1: Collect all metrics from monitors
            logger.LogInformation("Collecting metrics from monitors...");

            var throughputSamples = deps.GetThroughputSamples();
            var processMetrics = deps.GetProcessMetrics();
            var systemMetrics = deps.GetSystemMetrics();
            var rabbitMqMetrics = deps.GetRabbitMqMetrics();
            var postgresMetrics = deps.GetPostgresMetrics();

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
            var systemInfo = await deps.GetSystemInfo();

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

            await deps.GenerateReport(config.ResultsFolder, testReport);

            logger.LogInformation("JSON reports generated");

            // Step 7: Generate chart
            logger.LogInformation("Generating chart to {Folder}", config.ResultsFolder);

            var chartPath = await deps.GenerateChart(config.ResultsFolder, testReport, logger);

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
