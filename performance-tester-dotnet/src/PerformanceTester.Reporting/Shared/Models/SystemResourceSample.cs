namespace PerformanceTester.Reporting;

/// <summary>
/// Represents a single system-wide resource measurement sample.
/// Contains extended memory metrics (used, total, percent) compared to container metrics.
/// </summary>
/// <remarks>
/// System metrics have additional memory breakdown fields that container metrics don't have:
/// - MemoryUsedMb: Currently used memory
/// - MemoryTotalMb: Total available memory
/// - MemoryPercent: Usage as percentage
/// Sampling interval: 500ms
/// </remarks>
public sealed record SystemResourceSample
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
    /// CPU usage percentage (0-100, can exceed 100 on multi-core systems).
    /// </summary>
    public required double CpuPercent { get; init; }

    /// <summary>
    /// Memory currently in use in megabytes.
    /// </summary>
    public required double MemoryUsedMb { get; init; }

    /// <summary>
    /// Total available memory in megabytes.
    /// </summary>
    public required double MemoryTotalMb { get; init; }

    /// <summary>
    /// Memory usage as percentage (0-100).
    /// </summary>
    public required double MemoryPercent { get; init; }
}
