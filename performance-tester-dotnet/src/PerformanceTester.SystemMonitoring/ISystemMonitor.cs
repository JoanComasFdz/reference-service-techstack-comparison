namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Service for monitoring system-wide CPU and memory usage.
/// Implemented as a BackgroundService that runs continuously until stopped.
/// </summary>
/// <remarks>
/// Key differences from IProcessMonitor:
/// - Monitors ENTIRE SYSTEM, not a single process
/// - Uses /proc/stat for CPU and /proc/meminfo for memory
/// - Has special WSL2 handling for accurate Windows host memory
/// - Deferred start pattern: call StartMonitoringAsync() after IHost.StartAsync()
/// </remarks>
public interface ISystemMonitor
{
    /// <summary>
    /// Gets whether this system is running in WSL2.
    /// </summary>
    bool IsWsl2 { get; }

    /// <summary>
    /// Gets the number of logical CPU cores.
    /// </summary>
    int CpuCount { get; }

    /// <summary>
    /// Starts system-wide monitoring.
    /// Must be called after IHost.StartAsync().
    /// </summary>
    /// <param name="progress">Optional progress reporter for phase updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task that completes when first sample is collected.</returns>
    Task StartMonitoringAsync(
        IProgress<SystemMonitorPhaseInfo>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all collected system metrics.
    /// Call this after test completion (after stopping IHost).
    /// </summary>
    /// <returns>Read-only collection of all system metrics captured during monitoring.</returns>
    IReadOnlyCollection<SystemMetrics> GetCollectedMetrics();
}
