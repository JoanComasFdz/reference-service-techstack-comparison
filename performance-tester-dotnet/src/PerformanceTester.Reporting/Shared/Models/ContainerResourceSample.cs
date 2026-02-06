namespace PerformanceTester.Reporting;

/// <summary>
/// Represents a single container or system resource measurement sample (input data from Phase 2).
/// Used for Docker container (RabbitMQ, PostgreSQL) and system-wide CPU/memory monitoring.
/// </summary>
/// <remarks>
/// Container/system metrics differ from process metrics:
/// - Uses memory_mb instead of memory_rss_mb
/// - Does NOT include threads field
/// - Container samples arrive at Docker's push rate (event-driven, not a fixed configured interval)
/// - System sampling interval: 500ms
/// </remarks>
public sealed record ContainerResourceSample
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
    /// Memory usage in megabytes.
    /// </summary>
    public required double MemoryMb { get; init; }
}
