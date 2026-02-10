using System.Text.Json;
using System.Text.Json.Serialization;
using PerformanceTester.Reporting.ReportGeneration.Utilities;
using PerformanceTester.Reporting.Shared.Utilities;

namespace PerformanceTester.Reporting.ReportGeneration;

/// <summary>
/// Default implementation of report generator.
/// Generates JSON test reports from performance test data.
/// </summary>
public sealed class ReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true, // 2-space indentation by default
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new Iso8601DateTimeConverter() }
    };

    /// <summary>
    /// Generates all report files (JSON, resource metrics, throughput metrics).
    /// </summary>
    /// <param name="outputDirectory">Directory to write report files.</param>
    /// <param name="testReport">Complete test report data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    public async Task GenerateReportAsync(
        string outputDirectory,
        TestReport testReport,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory cannot be null or empty.", nameof(outputDirectory));
        }

        if (testReport == null)
        {
            throw new ArgumentNullException(nameof(testReport));
        }

        // Ensure output directory exists
        Directory.CreateDirectory(outputDirectory);

        // Generate timestamp and sanitized name for filenames
        var timestamp = testReport.TestDate.ToString("yyyyMMdd_HHmmss");
        var sanitizedName = ProcessNameSanitizer.Sanitize(testReport.MonitoredProcess.Name);
        var baseFilename = $"test-report-{timestamp}-{sanitizedName}";

        // Generate all 7 JSON reports
        await GenerateMainReportAsync(outputDirectory, baseFilename, testReport, cancellationToken);
        await GenerateEventsThroughputReportAsync(outputDirectory, baseFilename, testReport, cancellationToken);
        await GenerateApiThroughputReportAsync(outputDirectory, baseFilename, testReport, cancellationToken);
        await GenerateProcessResourceMetricsReportAsync(outputDirectory, baseFilename, testReport, cancellationToken);
        await GenerateSystemMetricsReportAsync(outputDirectory, baseFilename, testReport, cancellationToken);
        await GenerateRabbitMqMetricsReportAsync(outputDirectory, baseFilename, testReport, cancellationToken);
        await GeneratePostgresMetricsReportAsync(outputDirectory, baseFilename, testReport, cancellationToken);
    }

    private static async Task GenerateMainReportAsync(
        string outputDirectory,
        string baseFilename,
        TestReport testReport,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(outputDirectory, $"{baseFilename}.json");

        // Main report timestamp format: "yyyy-MM-dd HH:mm:ss" (no timezone, space separator)
        var mainReport = new MainReportJson
        {
            TestDateFormatted = testReport.TestDate.ToString("yyyy-MM-dd HH:mm:ss"),
            TotalRuntimeSeconds = Math.Round(testReport.TotalRuntimeSeconds, 3),
            System = testReport.System,
            PhaseTimestamps = new PhaseTimestamps
            {
                Phase1Start = Math.Round(testReport.PhaseTimestamps.Phase1Start, 3),
                Phase1End = Math.Round(testReport.PhaseTimestamps.Phase1End, 3),
                Phase2Start = Math.Round(testReport.PhaseTimestamps.Phase2Start, 3),
                Phase2End = Math.Round(testReport.PhaseTimestamps.Phase2End, 3),
                Phase3Start = Math.Round(testReport.PhaseTimestamps.Phase3Start, 3),
                Phase3End = Math.Round(testReport.PhaseTimestamps.Phase3End, 3)
            },
            MonitoredProcess = testReport.MonitoredProcess,
            Configuration = testReport.Configuration,
            Results = new TestResults
            {
                Phase1Publish = new PublishResults
                {
                    DurationSeconds = Math.Round(testReport.Results.Phase1Publish.DurationSeconds, 3),
                    ThroughputEventsPerSec = Math.Round(testReport.Results.Phase1Publish.ThroughputEventsPerSec, 2)
                },
                Phase2Consume = new ConsumeResults
                {
                    DurationSeconds = Math.Round(testReport.Results.Phase2Consume.DurationSeconds, 3),
                    ThroughputEventsPerSec = Math.Round(testReport.Results.Phase2Consume.ThroughputEventsPerSec, 2)
                },
                Phase3Api = new ApiResults
                {
                    DurationSeconds = Math.Round(testReport.Results.Phase3Api.DurationSeconds, 3),
                    TotalRequests = testReport.Results.Phase3Api.TotalRequests,
                    ThroughputCallsPerSec = Math.Round(testReport.Results.Phase3Api.ThroughputCallsPerSec, 2),
                    SuccessCount = testReport.Results.Phase3Api.SuccessCount,
                    SuccessPercentage = Math.Round(testReport.Results.Phase3Api.SuccessPercentage, 1),
                    ErrorCount = testReport.Results.Phase3Api.ErrorCount,
                    ErrorPercentage = Math.Round(testReport.Results.Phase3Api.ErrorPercentage, 1)
                }
            }
        };

        var json = JsonSerializer.Serialize(mainReport, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    private static async Task GenerateEventsThroughputReportAsync(
        string outputDirectory,
        string baseFilename,
        TestReport testReport,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(outputDirectory, $"{baseFilename}.events-throughput.json");

        // Calculate statistics from samples
        var summary = StatisticsCalculator.CalculateThroughputSummary(
            testReport.EventsThroughputSamples,
            s => s.Rate,
            s => s.CumulativeCount);

        var report = new EventsThroughputReportJson
        {
            TestDateFormatted = testReport.TestDate.ToString("yyyy-MM-dd HH:mm:ss"),
            SamplingIntervalMs = 100,
            Samples = testReport.EventsThroughputSamples.Select(s => new ThroughputSampleJson
            {
                Timestamp = s.Timestamp,
                ElapsedSeconds = Math.Round(s.ElapsedSeconds, 3),
                EventsPerSecond = Math.Round(s.Rate, 2),
                TotalEvents = s.CumulativeCount
            }).ToList(),
            Summary = new EventsThroughputSummaryJson
            {
                AvgEventsPerSecond = summary.AvgRate,
                PeakEventsPerSecond = summary.PeakRate,
                MinEventsPerSecond = summary.MinRate,
                StdDevEventsPerSecond = summary.StdDevRate,
                CvEventsPerSecond = summary.CvRate,
                AvgResponseTimeMs = summary.AvgResponseTimeMs,
                TotalSamples = summary.TotalSamples,
                TotalEvents = summary.TotalCount
            }
        };

        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    private static async Task GenerateApiThroughputReportAsync(
        string outputDirectory,
        string baseFilename,
        TestReport testReport,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(outputDirectory, $"{baseFilename}.api-throughput.json");

        // Calculate statistics from samples
        var summary = StatisticsCalculator.CalculateThroughputSummary(
            testReport.ApiThroughputSamples,
            s => s.Rate,
            s => s.CumulativeCount);

        var report = new ApiThroughputReportJson
        {
            TestDateFormatted = testReport.TestDate.ToString("yyyy-MM-dd HH:mm:ss"),
            SamplingIntervalMs = 100,
            Samples = testReport.ApiThroughputSamples.Select(s => new ApiThroughputSampleJson
            {
                Timestamp = s.Timestamp,
                ElapsedSeconds = Math.Round(s.ElapsedSeconds, 3),
                TotalCalls = s.CumulativeCount,
                CallsPerSecond = Math.Round(s.Rate, 2)
            }).ToList(),
            Summary = new ApiThroughputSummaryJson
            {
                AvgCallsPerSecond = summary.AvgRate,
                PeakCallsPerSecond = summary.PeakRate,
                MinCallsPerSecond = summary.MinRate,
                StdDevCallsPerSecond = summary.StdDevRate,
                CvCallsPerSecond = summary.CvRate,
                AvgResponseTimeMs = summary.AvgResponseTimeMs,
                TotalSamples = summary.TotalSamples,
                TotalCalls = summary.TotalCount
            }
        };

        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    private static async Task GenerateProcessResourceMetricsReportAsync(
        string outputDirectory,
        string baseFilename,
        TestReport testReport,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(outputDirectory, $"{baseFilename}.resource-metrics.json");

        var cpuValues = testReport.ProcessResourceSamples.Select(s => s.CpuPercent).ToList();
        var memoryValues = testReport.ProcessResourceSamples.Select(s => s.MemoryRssMb).ToList();

        var cpuSummary = StatisticsCalculator.CalculateResourceSummary(cpuValues, "%");
        var memorySummary = StatisticsCalculator.CalculateResourceSummary(memoryValues, "MB");

        var report = new ProcessResourceReportJson
        {
            TestDateFormatted = testReport.TestDate.ToString("yyyy-MM-dd HH:mm:ss"),
            ProcessInfo = new ProcessInfoJson
            {
                Pid = testReport.MonitoredProcess.Pid,
                Name = testReport.MonitoredProcess.Name,
                Port = testReport.MonitoredProcess.Port
            },
            SamplingIntervalMs = 500,
            Samples = testReport.ProcessResourceSamples.Select(s => new ProcessResourceSample
            {
                Timestamp = s.Timestamp,
                ElapsedSeconds = Math.Round(s.ElapsedSeconds, 3),
                CpuPercent = Math.Round(s.CpuPercent, 2),
                MemoryRssMb = Math.Round(s.MemoryRssMb, 2),
                Threads = s.Threads
            }).ToList(),
            Summary = new ProcessResourceSummaryJson
            {
                AvgCpuPercent = cpuSummary.Avg,
                PeakCpuPercent = cpuSummary.Max,
                AvgMemoryRssMb = memorySummary.Avg,
                PeakMemoryRssMb = memorySummary.Max,
                TotalSamples = testReport.ProcessResourceSamples.Count
            }
        };

        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    private static async Task GenerateSystemMetricsReportAsync(
        string outputDirectory,
        string baseFilename,
        TestReport testReport,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(outputDirectory, $"{baseFilename}.system-metrics.json");

        var cpuValues = testReport.SystemResourceSamples.Select(s => s.CpuPercent).ToList();
        var memoryUsedValues = testReport.SystemResourceSamples.Select(s => s.MemoryUsedMb).ToList();
        var memoryPercentValues = testReport.SystemResourceSamples.Select(s => s.MemoryPercent).ToList();

        var cpuSummary = StatisticsCalculator.CalculateResourceSummary(cpuValues, "%");
        var memoryUsedSummary = StatisticsCalculator.CalculateResourceSummary(memoryUsedValues, "MB");
        var memoryPercentSummary = StatisticsCalculator.CalculateResourceSummary(memoryPercentValues, "%");

        // Determine if running in WSL2 based on system info
        var isWsl2 = testReport.System.WslVersion == "WSL2";

        var report = new SystemMetricsReportJson
        {
            TestDateFormatted = testReport.TestDate.ToString("yyyy-MM-dd HH:mm:ss"),
            CpuCount = testReport.System.Cpu.LogicalProcessors,
            SamplingIntervalMs = 500,
            Samples = testReport.SystemResourceSamples.Select(s => new SystemResourceSample
            {
                Timestamp = s.Timestamp,
                ElapsedSeconds = Math.Round(s.ElapsedSeconds, 3),
                CpuPercent = Math.Round(s.CpuPercent, 2),
                MemoryUsedMb = Math.Round(s.MemoryUsedMb, 2),
                MemoryTotalMb = Math.Round(s.MemoryTotalMb, 2),
                MemoryPercent = Math.Round(s.MemoryPercent, 2)
            }).ToList(),
            Summary = new SystemMetricsSummaryJson
            {
                AvgCpuPercent = cpuSummary.Avg,
                PeakCpuPercent = cpuSummary.Max,
                MinCpuPercent = cpuSummary.Min,
                AvgMemoryUsedMb = memoryUsedSummary.Avg,
                PeakMemoryUsedMb = memoryUsedSummary.Max,
                AvgMemoryPercent = memoryPercentSummary.Avg,
                PeakMemoryPercent = memoryPercentSummary.Max,
                TotalSamples = testReport.SystemResourceSamples.Count
            },
            IsWsl2 = isWsl2
        };

        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    private static async Task GenerateRabbitMqMetricsReportAsync(
        string outputDirectory,
        string baseFilename,
        TestReport testReport,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(outputDirectory, $"{baseFilename}.rabbitmq-metrics.json");

        var cpuValues = testReport.RabbitMqResourceSamples.Select(s => s.CpuPercent).ToList();
        var memoryValues = testReport.RabbitMqResourceSamples.Select(s => s.MemoryMb).ToList();

        var cpuSummary = StatisticsCalculator.CalculateResourceSummary(cpuValues, "%");
        var memorySummary = StatisticsCalculator.CalculateResourceSummary(memoryValues, "MB");

        // Build container info if available, use defaults if not
        var containerInfo = testReport.RabbitMqContainerInfo ?? new ContainerInfo
        {
            Name = "performancetest-rabbitmq",
            Id = "unknown"
        };

        var report = new ContainerMetricsReportJson
        {
            TestDateFormatted = testReport.TestDate.ToString("yyyy-MM-dd HH:mm:ss"),
            ContainerInfo = containerInfo,
            Samples = testReport.RabbitMqResourceSamples.Select(s => new ContainerResourceSample
            {
                Timestamp = s.Timestamp,
                ElapsedSeconds = Math.Round(s.ElapsedSeconds, 3),
                CpuPercent = Math.Round(s.CpuPercent, 2),
                MemoryMb = Math.Round(s.MemoryMb, 2)
            }).ToList(),
            Summary = new ContainerResourceSummaryJson
            {
                AvgCpuPercent = cpuSummary.Avg,
                PeakCpuPercent = cpuSummary.Max,
                AvgMemoryMb = memorySummary.Avg,
                PeakMemoryMb = memorySummary.Max,
                TotalSamples = testReport.RabbitMqResourceSamples.Count
            }
        };

        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    private static async Task GeneratePostgresMetricsReportAsync(
        string outputDirectory,
        string baseFilename,
        TestReport testReport,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(outputDirectory, $"{baseFilename}.postgres-metrics.json");

        var cpuValues = testReport.PostgresResourceSamples.Select(s => s.CpuPercent).ToList();
        var memoryValues = testReport.PostgresResourceSamples.Select(s => s.MemoryMb).ToList();

        var cpuSummary = StatisticsCalculator.CalculateResourceSummary(cpuValues, "%");
        var memorySummary = StatisticsCalculator.CalculateResourceSummary(memoryValues, "MB");

        // Build container info if available, use defaults if not
        var containerInfo = testReport.PostgresContainerInfo ?? new ContainerInfo
        {
            Name = "performancetest-postgres",
            Id = "unknown"
        };

        var report = new ContainerMetricsReportJson
        {
            TestDateFormatted = testReport.TestDate.ToString("yyyy-MM-dd HH:mm:ss"),
            ContainerInfo = containerInfo,
            Samples = testReport.PostgresResourceSamples.Select(s => new ContainerResourceSample
            {
                Timestamp = s.Timestamp,
                ElapsedSeconds = Math.Round(s.ElapsedSeconds, 3),
                CpuPercent = Math.Round(s.CpuPercent, 2),
                MemoryMb = Math.Round(s.MemoryMb, 2)
            }).ToList(),
            Summary = new ContainerResourceSummaryJson
            {
                AvgCpuPercent = cpuSummary.Avg,
                PeakCpuPercent = cpuSummary.Max,
                AvgMemoryMb = memorySummary.Avg,
                PeakMemoryMb = memorySummary.Max,
                TotalSamples = testReport.PostgresResourceSamples.Count
            }
        };

        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }
}
