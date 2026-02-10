using System.Text.Json.Serialization;

namespace PerformanceTester.Reporting;

/// <summary>
/// System-wide resource metrics report with samples.
/// Used for both serialization (ReportGenerator) and deserialization (CompareCommand).
/// </summary>
public sealed record SystemMetricsReport
{
    /// <summary>
    /// Test date/time.
    /// </summary>
    public required DateTime TestDate { get; init; }

    /// <summary>
    /// Number of logical CPU cores.
    /// </summary>
    public required int CpuCount { get; init; }

    /// <summary>
    /// Sampling interval in milliseconds.
    /// </summary>
    public required int SamplingIntervalMs { get; init; }

    /// <summary>
    /// Individual system resource samples (time-series data).
    /// Uses SystemResourceSample directly — no separate JSON DTO needed.
    /// </summary>
    public required IReadOnlyList<SystemResourceSample> Samples { get; init; }

    /// <summary>
    /// Whether the system is running under WSL2.
    /// </summary>
    public bool IsWsl2 { get; init; }
}

/// <summary>
/// System metrics report for JSON serialization.
/// </summary>
public sealed record SystemMetricsReportJson
{
    [JsonPropertyName("test_date")]
    public required string TestDateFormatted { get; init; }

    public required int CpuCount { get; init; }

    public required int SamplingIntervalMs { get; init; }

    public required IReadOnlyList<SystemResourceSample> Samples { get; init; }

    public required SystemMetricsSummaryJson Summary { get; init; }

    public required bool IsWsl2 { get; init; }
}

/// <summary>
/// System-wide metrics summary for JSON serialization.
/// Includes CPU, memory usage, and memory percentage metrics.
/// </summary>
public sealed record SystemMetricsSummaryJson
{
    public required double AvgCpuPercent { get; init; }
    public required double PeakCpuPercent { get; init; }
    public required double MinCpuPercent { get; init; }
    public required double AvgMemoryUsedMb { get; init; }
    public required double PeakMemoryUsedMb { get; init; }
    public required double AvgMemoryPercent { get; init; }
    public required double PeakMemoryPercent { get; init; }
    public required int TotalSamples { get; init; }
}
