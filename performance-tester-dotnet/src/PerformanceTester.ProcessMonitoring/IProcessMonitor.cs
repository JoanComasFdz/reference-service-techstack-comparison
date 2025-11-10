namespace PerformanceTester.ProcessMonitoring;

/// <summary>
/// Service for monitoring process resource usage (CPU, memory, threads).
/// Implemented as a BackgroundService that runs continuously until stopped.
/// </summary>
public interface IProcessMonitor
{
    /// <summary>
    /// Gets the process ID being monitored.
    /// </summary>
    int ProcessId { get; }

    /// <summary>
    /// Gets all collected process metrics.
    /// Call this after test completion (after stopping IHost).
    /// </summary>
    /// <returns>Read-only collection of all process metrics captured during monitoring.</returns>
    IReadOnlyCollection<ProcessMetrics> GetCollectedMetrics();
}
