namespace PerformanceTester.EventConsuming;

/// <summary>
/// Service for collecting and retrieving throughput metrics from event consuming.
/// Implemented as a BackgroundService that reads from Channel&lt;EventThroughputSample&gt;.
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    /// Gets all collected event throughput samples.
    /// Call this after test completion (after stopping IHost).
    /// </summary>
    /// <returns>Read-only collection of all event throughput samples captured during test.</returns>
    IReadOnlyCollection<EventThroughputSample> GetThroughputSamples();
}
