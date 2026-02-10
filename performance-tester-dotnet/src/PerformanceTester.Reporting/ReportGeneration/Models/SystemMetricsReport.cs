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
