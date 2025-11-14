namespace PerformanceTester.Reporting;

/// <summary>
/// Represents a single throughput measurement sample (input data from Phase 2).
/// Used for events throughput and API throughput monitoring.
/// </summary>
/// <remarks>
/// This is the input model that will be collected by Phase 2 monitoring slices.
/// The ReportGenerator transforms this into ThroughputSampleJson for report output.
/// </remarks>
public sealed record ThroughputSample
{
    /// <summary>
    /// Sample timestamp (UTC).
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Elapsed seconds since test start.
    /// </summary>
    public required double ElapsedSeconds { get; init; }

    /// <summary>
    /// Throughput rate at this sample time (events/s or API calls/s).
    /// </summary>
    public required double Rate { get; init; }

    /// <summary>
    /// Cumulative count of events or API calls processed so far.
    /// </summary>
    public required int CumulativeCount { get; init; }
}
