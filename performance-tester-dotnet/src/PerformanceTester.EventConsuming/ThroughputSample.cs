namespace PerformanceTester.EventConsuming;

/// <summary>
/// Represents a throughput measurement sample at a specific point in time.
/// This model is owned by the EventConsuming slice (producer-owned contract).
/// </summary>
/// <param name="Timestamp">When this sample was captured (UTC).</param>
/// <param name="ThroughputEventsPerSecond">Events processed per second at this sample time.</param>
/// <param name="CumulativeEventCount">Total events processed since consumer started.</param>
public record ThroughputSample(
    DateTimeOffset Timestamp,
    double ThroughputEventsPerSecond,
    int CumulativeEventCount);
