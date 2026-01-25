namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Provides access to Docker container metrics collected during monitoring.
/// Supports deferred start pattern - monitoring begins when StartMonitoring() is called.
/// </summary>
public interface IDockerMonitor
{
    /// <summary>
    /// Gets the name of the container being monitored.
    /// </summary>
    string ContainerName { get; }

    /// <summary>
    /// Warms up the Docker API connection by making a test call.
    /// This should be called during setup to avoid slow first calls during the actual test.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task that completes when warmup is done.</returns>
    Task WarmupAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts monitoring the container and waits for the first sample to be collected.
    /// </summary>
    /// <remarks>
    /// This method implements the deferred start pattern. The BackgroundService starts
    /// when IHost.StartAsync() is called, but waits for this method before collecting metrics.
    /// This method blocks until the first sample is collected, ensuring metrics are aligned
    /// with the test start time.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task that completes when the first sample has been collected.</returns>
    /// <exception cref="InvalidOperationException">Thrown if monitoring has already been started.</exception>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all collected metrics for this container.
    /// Should be called after monitoring has stopped.
    /// </summary>
    /// <returns>Read-only collection of metrics in chronological order.</returns>
    IReadOnlyCollection<DockerMetrics> GetCollectedMetrics();
}
