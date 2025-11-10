namespace PerformanceTester.EventConsuming;

/// <summary>
/// Service for collecting and retrieving throughput metrics from event consuming.
/// Implemented as a BackgroundService that reads from Channel&lt;ThroughputSample&gt;.
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    /// Gets all collected throughput samples.
    /// Call this after test completion (after stopping IHost).
    /// </summary>
    /// <returns>Read-only collection of all throughput samples captured during test.</returns>
    IReadOnlyCollection<ThroughputSample> GetThroughputSamples();
}
