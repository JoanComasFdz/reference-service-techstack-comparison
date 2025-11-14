namespace PerformanceTester.Reporting;

/// <summary>
/// Represents a single process resource measurement sample (input data from Phase 2).
/// Used for monitored service process CPU and memory monitoring.
/// </summary>
/// <remarks>
/// Process metrics differ from container metrics:
/// - Uses memory_rss_mb (Resident Set Size) instead of memory_mb
/// - Includes threads field
/// - Sampling interval: 500ms
/// </remarks>
public sealed record ProcessResourceSample
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
    /// CPU usage percentage (0-100 per core, can exceed 100 on multi-core).
    /// </summary>
    public required double CpuPercent { get; init; }

    /// <summary>
    /// Memory RSS (Resident Set Size) in megabytes.
    /// </summary>
    public required double MemoryRssMb { get; init; }

    /// <summary>
    /// Number of threads.
    /// </summary>
    public required int Threads { get; init; }
}
