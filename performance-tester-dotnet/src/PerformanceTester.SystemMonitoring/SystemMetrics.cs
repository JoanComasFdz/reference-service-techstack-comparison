namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Represents system-wide resource metrics at a specific point in time.
/// This model is owned by the SystemMonitoring slice (producer-owned contract).
/// </summary>
/// <remarks>
/// This differs from ProcessMetrics:
/// - Tracks ENTIRE SYSTEM CPU/memory, not a single process
/// - Has memory breakdown: used, total, percent
/// - No thread count (system-wide threads is not meaningful)
/// </remarks>
/// <param name="Timestamp">When this sample was captured (UTC).</param>
/// <param name="ElapsedSeconds">Seconds since monitoring started.</param>
/// <param name="CpuPercent">System-wide CPU usage percentage (0-100).</param>
/// <param name="MemoryUsedMb">Memory currently in use (megabytes).</param>
/// <param name="MemoryTotalMb">Total system memory (megabytes).</param>
/// <param name="MemoryPercent">Memory usage as percentage (0-100).</param>
public sealed record SystemMetrics(
    DateTimeOffset Timestamp,
    double ElapsedSeconds,
    double CpuPercent,
    double MemoryUsedMb,
    double MemoryTotalMb,
    double MemoryPercent);
