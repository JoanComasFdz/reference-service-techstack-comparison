using System.Text.Json;
using System.Text.Json.Serialization;
using PerformanceTester.Reporting.ReportGeneration.Utilities;
using PerformanceTester.Reporting.Shared.Utilities;

namespace PerformanceTester.Reporting.ReportGeneration;

/// <summary>
/// Default implementation of report generator.
/// Generates JSON test reports from performance test data.
/// </summary>
internal sealed class ReportGenerator : IReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true, // 2-space indentation by default
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new Iso8601DateTimeConverter() }
    };

    /// <inheritdoc />
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
        var mainReport = new
        {
            test_date = testReport.TestDate.ToString("yyyy-MM-dd HH:mm:ss"),
            total_runtime_seconds = Math.Round(testReport.TotalRuntimeSeconds, 3),
            system = testReport.System,
            phase_timestamps = new
            {
                phase1_start = Math.Round(testReport.PhaseTimestamps.Phase1Start, 3),
                phase1_end = Math.Round(testReport.PhaseTimestamps.Phase1End, 3),
                phase2_start = Math.Round(testReport.PhaseTimestamps.Phase2Start, 3),
                phase2_end = Math.Round(testReport.PhaseTimestamps.Phase2End, 3),
                phase3_start = Math.Round(testReport.PhaseTimestamps.Phase3Start, 3),
                phase3_end = Math.Round(testReport.PhaseTimestamps.Phase3End, 3)
            },
            monitored_process = new
            {
                name = testReport.MonitoredProcess.Name,
                pid = testReport.MonitoredProcess.Pid,
                port = testReport.MonitoredProcess.Port
            },
            configuration = new
            {
                num_events = testReport.Configuration.NumEvents,
                api_duration = testReport.Configuration.ApiDuration,
                api_concurrent_workers = testReport.Configuration.ApiConcurrentWorkers,
                rabbitmq_exchange = testReport.Configuration.RabbitmqExchange,
                consumer_queue = testReport.Configuration.ConsumerQueue,
                api_endpoint = testReport.Configuration.ApiEndpoint,
                publish_event_type = testReport.Configuration.PublishEventType,
                consume_event_type = testReport.Configuration.ConsumeEventType
            },
            results = new
            {
                phase1_publish = new
                {
                    duration_seconds = Math.Round(testReport.Results.Phase1Publish.DurationSeconds, 3),
                    throughput_events_per_sec = Math.Round(testReport.Results.Phase1Publish.ThroughputEventsPerSec, 2)
                },
                phase2_consume = new
                {
                    duration_seconds = Math.Round(testReport.Results.Phase2Consume.DurationSeconds, 3),
                    throughput_events_per_sec = Math.Round(testReport.Results.Phase2Consume.ThroughputEventsPerSec, 2)
                },
                phase3_api = new
                {
                    duration_seconds = Math.Round(testReport.Results.Phase3Api.DurationSeconds, 3),
                    total_requests = testReport.Results.Phase3Api.TotalRequests,
                    throughput_calls_per_sec = Math.Round(testReport.Results.Phase3Api.ThroughputCallsPerSec, 2),
                    success_count = testReport.Results.Phase3Api.SuccessCount,
                    success_percentage = Math.Round(testReport.Results.Phase3Api.SuccessPercentage, 1),
                    error_count = testReport.Results.Phase3Api.ErrorCount,
                    error_percentage = Math.Round(testReport.Results.Phase3Api.ErrorPercentage, 1)
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

        // Use anonymous type with events-specific field names
        var report = new
        {
            test_date = testReport.TestDate.ToString("yyyy-MM-dd HH:mm:ss"),
            sampling_interval_ms = 100,
            samples = testReport.EventsThroughputSamples.Select(s => new
            {
                timestamp = s.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.ffffff"),
                elapsed_seconds = Math.Round(s.ElapsedSeconds, 3),
                total_events = s.CumulativeCount,
                events_per_second = Math.Round(s.Rate, 2)
            }).ToList(),
            summary = new
            {
                avg_events_per_second = summary.AvgRate,
                peak_events_per_second = summary.PeakRate,
                min_events_per_second = summary.MinRate,
                std_dev_events_per_second = summary.StdDevRate,
                cv_events_per_second = summary.CvRate,
                avg_response_time_ms = summary.AvgResponseTimeMs,
                total_samples = summary.TotalSamples,
                total_events = summary.TotalCount
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

        // Use anonymous type with API-specific field names (calls instead of events)
        var report = new
        {
            test_date = testReport.TestDate.ToString("yyyy-MM-dd HH:mm:ss"),
            sampling_interval_ms = 100,
            samples = testReport.ApiThroughputSamples.Select(s => new
            {
                timestamp = s.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.ffffff"),
                elapsed_seconds = Math.Round(s.ElapsedSeconds, 3),
                total_calls = s.CumulativeCount,
                calls_per_second = Math.Round(s.Rate, 2)
            }).ToList(),
            summary = new
            {
                avg_calls_per_second = summary.AvgRate,
                peak_calls_per_second = summary.PeakRate,
                min_calls_per_second = summary.MinRate,
                std_dev_calls_per_second = summary.StdDevRate,
                cv_calls_per_second = summary.CvRate,
                avg_response_time_ms = summary.AvgResponseTimeMs,
                total_samples = summary.TotalSamples,
                total_calls = summary.TotalCount
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

        var report = new
        {
            test_date = testReport.TestDate.ToString("yyyy-MM-dd HH:mm:ss"),
            process_info = new
            {
                pid = testReport.MonitoredProcess.Pid,
                name = testReport.MonitoredProcess.Name,
                port = testReport.MonitoredProcess.Port
            },
            sampling_interval_ms = 500,
            samples = testReport.ProcessResourceSamples.Select(s => new
            {
                timestamp = s.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.ffffff"),
                cpu_percent = Math.Round(s.CpuPercent, 2),
                memory_rss_mb = Math.Round(s.MemoryRssMb, 2),
                threads = s.Threads
            }).ToList(),
            summary = new
            {
                avg_cpu_percent = cpuSummary.Avg,
                peak_cpu_percent = cpuSummary.Max,
                avg_memory_rss_mb = memorySummary.Avg,
                peak_memory_rss_mb = memorySummary.Max,
                total_samples = testReport.ProcessResourceSamples.Count
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

        var report = new
        {
            test_date = testReport.TestDate.ToString("yyyy-MM-dd HH:mm:ss"),
            cpu_count = testReport.System.Cpu.LogicalProcessors,
            sampling_interval_ms = 500,
            samples = testReport.SystemResourceSamples.Select(s => new
            {
                timestamp = s.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.ffffff"),
                cpu_percent = Math.Round(s.CpuPercent, 2),
                memory_used_mb = Math.Round(s.MemoryUsedMb, 2),
                memory_total_mb = Math.Round(s.MemoryTotalMb, 2),
                memory_percent = Math.Round(s.MemoryPercent, 2)
            }).ToList(),
            summary = new
            {
                avg_cpu_percent = cpuSummary.Avg,
                peak_cpu_percent = cpuSummary.Max,
                min_cpu_percent = cpuSummary.Min,
                avg_memory_used_mb = memoryUsedSummary.Avg,
                peak_memory_used_mb = memoryUsedSummary.Max,
                avg_memory_percent = memoryPercentSummary.Avg,
                peak_memory_percent = memoryPercentSummary.Max,
                total_samples = testReport.SystemResourceSamples.Count
            },
            is_wsl2 = isWsl2
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

        var report = new
        {
            test_date = testReport.TestDate.ToString("yyyy-MM-dd HH:mm:ss"),
            container_info = new
            {
                name = containerInfo.Name,
                id = containerInfo.Id
            },
            samples = testReport.RabbitMqResourceSamples.Select(s => new
            {
                timestamp = s.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.ffffff"),
                cpu_percent = Math.Round(s.CpuPercent, 2),
                memory_mb = Math.Round(s.MemoryMb, 2)
            }).ToList(),
            summary = new
            {
                avg_cpu_percent = cpuSummary.Avg,
                peak_cpu_percent = cpuSummary.Max,
                avg_memory_mb = memorySummary.Avg,
                peak_memory_mb = memorySummary.Max,
                total_samples = testReport.RabbitMqResourceSamples.Count
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

        var report = new
        {
            test_date = testReport.TestDate.ToString("yyyy-MM-dd HH:mm:ss"),
            container_info = new
            {
                name = containerInfo.Name,
                id = containerInfo.Id
            },
            samples = testReport.PostgresResourceSamples.Select(s => new
            {
                timestamp = s.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.ffffff"),
                cpu_percent = Math.Round(s.CpuPercent, 2),
                memory_mb = Math.Round(s.MemoryMb, 2)
            }).ToList(),
            summary = new
            {
                avg_cpu_percent = cpuSummary.Avg,
                peak_cpu_percent = cpuSummary.Max,
                avg_memory_mb = memorySummary.Avg,
                peak_memory_mb = memorySummary.Max,
                total_samples = testReport.PostgresResourceSamples.Count
            }
        };

        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }
}
