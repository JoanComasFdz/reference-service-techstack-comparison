using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PerformanceTester.Reporting.Shared.Utilities;

namespace PerformanceTester.Reporting.ChartGeneration.DataLoading;

/// <summary>
/// Loads chart data from JSON report files using typed deserialization.
/// </summary>
internal static class ChartDataLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new Iso8601DateTimeConverter() }
    };

    /// <summary>
    /// Loads events throughput report from JSON file.
    /// </summary>
    public static ThroughputReport? LoadEventsThroughputReport(string filePath, ILogger? logger = null)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            var report = JsonSerializer.Deserialize<EventsThroughputReportJson>(json, JsonOptions);
            if (report == null)
                return null;

            return new ThroughputReport
            {
                TestDate = DateTime.Parse(report.TestDateFormatted),
                SamplingIntervalMs = report.SamplingIntervalMs,
                Samples = report.Samples,
                Summary = new ThroughputSummary
                {
                    AvgRate = report.Summary.AvgEventsPerSecond,
                    PeakRate = report.Summary.PeakEventsPerSecond,
                    MinRate = report.Summary.MinEventsPerSecond,
                    StdDevRate = report.Summary.StdDevEventsPerSecond,
                    CvRate = report.Summary.CvEventsPerSecond,
                    AvgResponseTimeMs = report.Summary.AvgResponseTimeMs,
                    TotalSamples = report.Summary.TotalSamples,
                    TotalCount = report.Summary.TotalEvents
                }
            };
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load events throughput report from {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Loads API throughput report from JSON file.
    /// </summary>
    public static ThroughputReport? LoadApiThroughputReport(string filePath, ILogger? logger = null)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            var report = JsonSerializer.Deserialize<ApiThroughputReportJson>(json, JsonOptions);
            if (report == null)
                return null;

            return new ThroughputReport
            {
                TestDate = DateTime.Parse(report.TestDateFormatted),
                SamplingIntervalMs = report.SamplingIntervalMs,
                Samples = report.Samples.Select(s => new ThroughputSampleJson
                {
                    Timestamp = s.Timestamp,
                    ElapsedSeconds = s.ElapsedSeconds,
                    TotalEvents = s.TotalCalls,
                    EventsPerSecond = s.CallsPerSecond
                }).ToList(),
                Summary = new ThroughputSummary
                {
                    AvgRate = report.Summary.AvgCallsPerSecond,
                    PeakRate = report.Summary.PeakCallsPerSecond,
                    MinRate = report.Summary.MinCallsPerSecond,
                    StdDevRate = report.Summary.StdDevCallsPerSecond,
                    CvRate = report.Summary.CvCallsPerSecond,
                    AvgResponseTimeMs = report.Summary.AvgResponseTimeMs,
                    TotalSamples = report.Summary.TotalSamples,
                    TotalCount = report.Summary.TotalCalls
                }
            };
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load API throughput report from {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Loads container resource metrics report from JSON file.
    /// Used for RabbitMQ, PostgreSQL, and system metrics.
    /// </summary>
    public static ResourceMetricsReport? LoadContainerResourceReport(string filePath, ILogger? logger = null)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            var report = JsonSerializer.Deserialize<ContainerMetricsReportJson>(json, JsonOptions);
            if (report == null)
                return null;

            var cpuValues = report.Samples.Select(s => s.CpuPercent).ToList();
            var memoryValues = report.Samples.Select(s => s.MemoryMb).ToList();

            return new ResourceMetricsReport
            {
                TestDate = DateTime.Parse(report.TestDateFormatted),
                SamplingIntervalMs = 500,
                Samples = report.Samples.Select(s => new ResourceSampleJson
                {
                    Timestamp = s.Timestamp,
                    ElapsedSeconds = s.ElapsedSeconds,
                    CpuPercent = s.CpuPercent,
                    MemoryMb = s.MemoryMb
                }).ToList(),
                CpuSummary = new ResourceSummary
                {
                    Avg = report.Summary.AvgCpuPercent,
                    Min = cpuValues.Count > 0 ? cpuValues.Min() : 0,
                    Max = report.Summary.PeakCpuPercent,
                    Mode = CalculateMode(cpuValues),
                    Unit = "%"
                },
                MemorySummary = new ResourceSummary
                {
                    Avg = report.Summary.AvgMemoryMb,
                    Min = memoryValues.Count > 0 ? memoryValues.Min() : 0,
                    Max = report.Summary.PeakMemoryMb,
                    Mode = CalculateMode(memoryValues),
                    Unit = "MB"
                }
            };
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load container resource report from {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Loads system metrics report from JSON file.
    /// </summary>
    public static ResourceMetricsReport? LoadSystemResourceReport(string filePath, ILogger? logger = null)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            var report = JsonSerializer.Deserialize<SystemMetricsReportJson>(json, JsonOptions);
            if (report == null)
                return null;

            var cpuValues = report.Samples.Select(s => s.CpuPercent).ToList();
            var memoryValues = report.Samples.Select(s => s.MemoryUsedMb).ToList();

            return new ResourceMetricsReport
            {
                TestDate = DateTime.Parse(report.TestDateFormatted),
                SamplingIntervalMs = report.SamplingIntervalMs,
                Samples = report.Samples.Select(s => new ResourceSampleJson
                {
                    Timestamp = s.Timestamp,
                    ElapsedSeconds = s.ElapsedSeconds,
                    CpuPercent = s.CpuPercent,
                    MemoryMb = s.MemoryUsedMb
                }).ToList(),
                CpuSummary = new ResourceSummary
                {
                    Avg = report.Summary.AvgCpuPercent,
                    Min = cpuValues.Count > 0 ? cpuValues.Min() : 0,
                    Max = report.Summary.PeakCpuPercent,
                    Mode = CalculateMode(cpuValues),
                    Unit = "%"
                },
                MemorySummary = new ResourceSummary
                {
                    Avg = report.Summary.AvgMemoryUsedMb,
                    Min = memoryValues.Count > 0 ? memoryValues.Min() : 0,
                    Max = report.Summary.PeakMemoryUsedMb,
                    Mode = CalculateMode(memoryValues),
                    Unit = "MB"
                }
            };
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load system resource report from {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Loads process resource metrics report from JSON file.
    /// Used for service (monitored process) metrics.
    /// </summary>
    public static ResourceMetricsReport? LoadProcessResourceReport(string filePath, ILogger? logger = null)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            var report = JsonSerializer.Deserialize<ProcessResourceReportJson>(json, JsonOptions);
            if (report == null)
                return null;

            var cpuValues = report.Samples.Select(s => s.CpuPercent).ToList();
            var memoryValues = report.Samples.Select(s => s.MemoryRssMb).ToList();

            return new ResourceMetricsReport
            {
                TestDate = DateTime.Parse(report.TestDateFormatted),
                SamplingIntervalMs = report.SamplingIntervalMs,
                Samples = report.Samples.Select(s => new ResourceSampleJson
                {
                    Timestamp = s.Timestamp,
                    ElapsedSeconds = s.ElapsedSeconds,
                    CpuPercent = s.CpuPercent,
                    MemoryMb = s.MemoryRssMb
                }).ToList(),
                CpuSummary = new ResourceSummary
                {
                    Avg = report.Summary.AvgCpuPercent,
                    Min = cpuValues.Count > 0 ? cpuValues.Min() : 0,
                    Max = report.Summary.PeakCpuPercent,
                    Mode = CalculateMode(cpuValues),
                    Unit = "%"
                },
                MemorySummary = new ResourceSummary
                {
                    Avg = report.Summary.AvgMemoryRssMb,
                    Min = memoryValues.Count > 0 ? memoryValues.Min() : 0,
                    Max = report.Summary.PeakMemoryRssMb,
                    Mode = CalculateMode(memoryValues),
                    Unit = "MB"
                }
            };
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load process resource report from {FilePath}", filePath);
            return null;
        }
    }

    private static int CalculateMode(List<double> values)
    {
        if (values.Count == 0)
            return 0;

        return values
            .Select(v => (int)Math.Round(v))
            .GroupBy(x => x)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First()
            .Key;
    }
}
