namespace PerformanceTester.ProcessMonitoring;

/// <summary>
/// Service for monitoring process resource usage (CPU, memory, threads).
/// Implemented as a BackgroundService that runs continuously until stopped.
/// Supports deferred start pattern where process ID is provided after IHost.StartAsync().
/// </summary>
public interface IProcessMonitor
{
    /// <summary>
    /// Starts monitoring the specified process.
    /// Must be called after IHost.StartAsync() and before monitoring can begin.
    /// </summary>
    /// <param name="processId">Process ID to monitor.</param>
    /// <param name="progress">Optional progress reporter for phase notifications.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if processId is not positive.</exception>
    /// <exception cref="InvalidOperationException">Thrown if monitoring has already been started.</exception>
    /// <returns>Task that completes when the first sample has been collected.</returns>
    /// <remarks>
    /// This method enables deferred process monitoring where the process ID is not known
    /// at DI registration time. Common in orchestration scenarios where:
    /// 1. DI container is built
    /// 2. IHost is started
    /// 3. Service under test is launched
    /// 4. Process ID is discovered
    /// 5. Monitoring begins via StartMonitoringAsync()
    ///
    /// The method returns after the first sample is collected, ensuring the caller
    /// can proceed knowing monitoring has actually started.
    /// </remarks>
    Task StartMonitoringAsync(
        int processId,
        IProgress<ProcessMonitorPhaseInfo>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the process ID being monitored.
    /// Returns null if StartMonitoring has not been called yet.
    /// </summary>
    int? ProcessId { get; }

    /// <summary>
    /// Gets all collected process metrics.
    /// Call this after test completion (after stopping IHost).
    /// </summary>
    /// <returns>Read-only collection of all process metrics captured during monitoring.</returns>
    IReadOnlyCollection<ProcessMetrics> GetCollectedMetrics();
}
