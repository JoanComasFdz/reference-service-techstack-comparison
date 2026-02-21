using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.EventPublishing;

/// <summary>
/// Metrics captured during event publishing.
/// </summary>
/// <param name="EventCount">Number of events published.</param>
/// <param name="Duration">Total duration for publishing all events.</param>
/// <param name="EventsPerSecond">Throughput (events/second).</param>
public record PublishMetrics(
    EventCount EventCount,
    TimeSpan Duration,
    EventsPerSecond EventsPerSecond);
