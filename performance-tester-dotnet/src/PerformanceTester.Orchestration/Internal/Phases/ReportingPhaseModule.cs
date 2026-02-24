using PerformanceTester.Functional;
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
using PerformanceTester.Orchestration;
using Serilog.Context;
using static PerformanceTester.Functional.Result<PerformanceTester.Reporting.TestReport, string>;

namespace PerformanceTester.Orchestration.Internal;

/// <summary>
/// Phase 3: Collect metrics, build report, generate JSON and chart.
/// Expects monitors to be already stopped by the orchestrator.
/// </summary>
internal static class ReportingPhaseModule
{
    /// <summary>
    /// Returns throughput samples collected during the event test.
    /// </summary>
    public delegate IReadOnlyCollection<EventThroughputSample> GetThroughputSamplesDelegate();

    /// <summary>
    /// Returns process resource metrics (CPU, memory, threads) collected during the test.
    /// </summary>
    public delegate IReadOnlyCollection<ProcessMetrics> GetProcessMetricsDelegate();

    /// <summary>
    /// Returns system-wide metrics (CPU, memory) collected during the test.
    /// </summary>
    public delegate IReadOnlyCollection<SystemMetrics> GetSystemMetricsDelegate();

    /// <summary>
    /// Returns Docker container metrics for the RabbitMQ container.
    /// </summary>
    public delegate IReadOnlyCollection<DockerMetrics> GetRabbitMqMetricsDelegate();

    /// <summary>
    /// Returns Docker container metrics for the PostgreSQL container.
    /// </summary>
    public delegate IReadOnlyCollection<DockerMetrics> GetPostgresMetricsDelegate();

    /// <summary>
    /// Detects and returns system hardware/OS information.
    /// </summary>
    public delegate Task<SystemInfo?> GetSystemInfoDelegate();

    /// <summary>
    /// Generates JSON report files to the specified output folder.
    /// </summary>
    public delegate Task GenerateReportDelegate(ResultsOutputFolder outputFolder, TestReport testReport);

    /// <summary>
    /// Generates a PNG chart to the specified output folder.
    /// Returns the full path to the generated chart file.
    /// </summary>
    public delegate Task<string> GenerateChartDelegate(ResultsOutputFolder outputFolder, TestReport testReport, ILogger logger);

    // -- Dependencies record (bundle of what I need) -------------------------------

    public record Dependencies(
        GetThroughputSamplesDelegate GetThroughputSamples,
        GetProcessMetricsDelegate GetProcessMetrics,
        GetSystemMetricsDelegate GetSystemMetrics,
        GetRabbitMqMetricsDelegate GetRabbitMqMetrics,
        GetPostgresMetricsDelegate GetPostgresMetrics,
        GetSystemInfoDelegate GetSystemInfo,
        GenerateReportDelegate GenerateReport,
        GenerateChartDelegate GenerateChart);

    // -- Factory (how to build what I need from DI) --------------------------------

    public static Dependencies BuildDependencies(
        IServiceProvider services,
        TestConfiguration config,
        CancellationToken ct)
    {
        var metricsCollector = services.GetRequiredService<IMetricsCollector>();
        var getProcessMetrics = services.GetRequiredService<ProcessMonitoring.GetProcessMetricsDelegate>();
        var systemMonitor = services.GetRequiredService<ISystemMonitor>();
        var getDockerMetrics = services.GetRequiredService<GetDockerMetricsDelegate>();
        var systemInfoDetector = services.GetRequiredService<ISystemInfoDetector>();
        var reportGenerator = services.GetRequiredService<ReportGenerator>();

        return new Dependencies(
            GetThroughputSamples: metricsCollector.GetThroughputSamples,
            GetProcessMetrics: () => getProcessMetrics(),
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

            if (systemInfo is null)
            {
                return new Failure("System info detection failed — cannot generate report without hardware/OS information");
            }

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
