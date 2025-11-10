namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Provides access to Docker container metrics collected during monitoring.
/// </summary>
public interface IDockerMonitor
{
    /// <summary>
    /// Gets the name of the container being monitored.
    /// </summary>
    string ContainerName { get; }

    /// <summary>
    /// Gets all collected metrics for this container.
    /// Should be called after monitoring has stopped.
    /// </summary>
    /// <returns>Read-only collection of metrics in chronological order.</returns>
    IReadOnlyCollection<DockerMetrics> GetCollectedMetrics();
}
