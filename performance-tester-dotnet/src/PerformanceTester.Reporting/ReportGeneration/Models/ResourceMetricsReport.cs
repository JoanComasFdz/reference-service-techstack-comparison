using System.Text.Json.Serialization;

namespace PerformanceTester.Reporting;

/// <summary>
/// Resource metrics report (CPU/RAM) with samples and statistical summary.
/// Used for process, container, and system-wide metrics.
/// </summary>
public sealed record ResourceMetricsReport
{
    /// <summary>
    /// Test date/time.
    /// </summary>
    public required DateTime TestDate { get; init; }

    /// <summary>
    /// Sampling interval in milliseconds.
    /// </summary>
    public required int SamplingIntervalMs { get; init; }

    /// <summary>
    /// Individual resource samples (time-series data).
    /// </summary>
    public required IReadOnlyList<ResourceSampleJson> Samples { get; init; }

    /// <summary>
    /// Statistical summary of CPU metrics.
    /// </summary>
    public required ResourceSummary CpuSummary { get; init; }

    /// <summary>
    /// Statistical summary of memory metrics.
    /// </summary>
    public required ResourceSummary MemorySummary { get; init; }
}

/// <summary>
/// Process-specific resource metrics report (CPU/RSS memory/threads) with samples and statistical summary.
/// </summary>
public sealed record ProcessResourceMetricsReport
{
    /// <summary>
    /// Test date/time.
    /// </summary>
    public required DateTime TestDate { get; init; }

    /// <summary>
    /// Sampling interval in milliseconds.
    /// </summary>
    public required int SamplingIntervalMs { get; init; }

    /// <summary>
    /// Individual resource samples (time-series data).
    /// Uses ProcessResourceSample directly — no separate JSON DTO needed.
    /// </summary>
    public required IReadOnlyList<ProcessResourceSample> Samples { get; init; }

    /// <summary>
    /// Statistical summary of CPU metrics.
    /// </summary>
    public ResourceSummary? CpuSummary { get; init; }

    /// <summary>
    /// Statistical summary of memory metrics.
    /// </summary>
    public ResourceSummary? MemorySummary { get; init; }
}

/// <summary>
/// Individual resource sample for JSON output.
/// </summary>
public sealed record ResourceSampleJson
{
    /// <summary>
    /// Sample timestamp (UTC).
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Elapsed seconds since test start.
    /// </summary>
    public required double ElapsedSeconds { get; init; }

    /// <summary>
    /// CPU usage percentage (0-100 per core, can exceed 100).
    /// </summary>
    public required double CpuPercent { get; init; }

    /// <summary>
    /// Memory usage in megabytes.
    /// </summary>
    public required double MemoryMb { get; init; }
}

/// <summary>
/// Statistical summary of a resource metric (CPU or memory).
/// </summary>
public sealed record ResourceSummary
{
    /// <summary>
    /// Average value.
    /// </summary>
    public required double Avg { get; init; }

    /// <summary>
    /// Minimum value.
    /// </summary>
    public required double Min { get; init; }

    /// <summary>
    /// Maximum value.
    /// </summary>
    public required double Max { get; init; }

    /// <summary>
    /// Most common value (mode) - rounded to nearest integer.
    /// </summary>
    public required int Mode { get; init; }

    /// <summary>
    /// Unit of measurement (e.g., "%", "MB").
    /// </summary>
    public required string Unit { get; init; }
}

/// <summary>
/// Process resource metrics report for JSON serialization.
/// </summary>
public sealed record ProcessResourceReportJson
{
    [JsonPropertyName("test_date")]
    public required string TestDateFormatted { get; init; }

    public required ProcessInfoJson ProcessInfo { get; init; }

    public required int SamplingIntervalMs { get; init; }

    public required IReadOnlyList<ProcessResourceSample> Samples { get; init; }

    public required ProcessResourceSummaryJson Summary { get; init; }
}

/// <summary>
/// Process information for resource metrics JSON serialization.
/// Property order (pid, name, port) matches Python output format.
/// </summary>
public sealed record ProcessInfoJson
{
    public required int Pid { get; init; }
    public required string Name { get; init; }
    public required int Port { get; init; }
}

/// <summary>
/// Process resource summary for JSON serialization.
/// Uses process-specific naming (memory_rss_mb).
/// </summary>
public sealed record ProcessResourceSummaryJson
{
    public required double AvgCpuPercent { get; init; }
    public required double PeakCpuPercent { get; init; }
    public required double AvgMemoryRssMb { get; init; }
    public required double PeakMemoryRssMb { get; init; }
    public required int TotalSamples { get; init; }
}

/// <summary>
/// Container metrics report for JSON serialization (RabbitMQ, PostgreSQL).
/// </summary>
public sealed record ContainerMetricsReportJson
{
    [JsonPropertyName("test_date")]
    public required string TestDateFormatted { get; init; }

    public required ContainerInfo ContainerInfo { get; init; }

    public required IReadOnlyList<ContainerResourceSample> Samples { get; init; }

    public required ContainerResourceSummaryJson Summary { get; init; }
}

/// <summary>
/// Container resource summary for JSON serialization (RabbitMQ, PostgreSQL).
/// Uses generic memory_mb naming.
/// </summary>
public sealed record ContainerResourceSummaryJson
{
    public required double AvgCpuPercent { get; init; }
    public required double PeakCpuPercent { get; init; }
    public required double AvgMemoryMb { get; init; }
    public required double PeakMemoryMb { get; init; }
    public required int TotalSamples { get; init; }
}
