namespace PerformanceTester.Reporting;

/// <summary>
/// Represents a single throughput metric sample (normalized data for reporting).
/// Used for both events throughput and API throughput in test reports.
/// </summary>
/// <remarks>
/// This is the normalized reporting model that aggregates throughput data from various sources.
/// The ReportGenerator transforms source-specific samples (EventThroughputSample, ApiThroughputSample)
/// into this common format for report output and comparison.
/// </remarks>
public sealed record ThroughputMetricSample
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
