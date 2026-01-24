using System.Text.Json.Serialization;

namespace PerformanceTester.Reporting;

/// <summary>
/// Throughput metrics report with samples and statistical summary.
/// Matches Python throughput report JSON format.
/// </summary>
public sealed record ThroughputReport
{
    /// <summary>
    /// Test date/time.
    /// </summary>
    public required string TestDate { get; init; }

    /// <summary>
    /// Sampling interval in milliseconds.
    /// </summary>
    public required int SamplingIntervalMs { get; init; }

    /// <summary>
    /// Individual throughput samples (time-series data).
    /// </summary>
    public required IReadOnlyList<ThroughputSampleJson> Samples { get; init; }

    /// <summary>
    /// Statistical summary of throughput data.
    /// </summary>
    public required ThroughputSummary Summary { get; init; }
}

/// <summary>
/// Individual throughput sample for JSON input/output.
/// Used for chart generation - stores normalized values.
/// </summary>
public sealed record ThroughputSampleJson
{
    /// <summary>
    /// Sample timestamp (ISO8601 format).
    /// </summary>
    public required string Timestamp { get; init; }

    /// <summary>
    /// Elapsed seconds since test start.
    /// </summary>
    public required double ElapsedSeconds { get; init; }

    /// <summary>
    /// Throughput rate (events/s or calls/s) - normalized from source data.
    /// </summary>
    public double EventsPerSecond { get; init; }

    /// <summary>
    /// Cumulative count (total events or calls) - normalized from source data.
    /// </summary>
    public int TotalEvents { get; init; }

    /// <summary>
    /// Gets the throughput rate for chart generation.
    /// </summary>
    [JsonIgnore]
    public double ThroughputRate => EventsPerSecond;

    /// <summary>
    /// Gets the cumulative count for chart generation.
    /// </summary>
    [JsonIgnore]
    public int CumulativeCount => TotalEvents;
}

/// <summary>
/// Statistical summary of throughput metrics.
/// Used both for calculation output and JSON serialization.
/// </summary>
public sealed record ThroughputSummary
{
    /// <summary>
    /// Average throughput rate.
    /// </summary>
    public required double AvgRate { get; init; }

    /// <summary>
    /// Peak (maximum) throughput rate.
    /// </summary>
    public required double PeakRate { get; init; }

    /// <summary>
    /// Minimum throughput rate (zero values excluded).
    /// </summary>
    public required double MinRate { get; init; }

    /// <summary>
    /// Standard deviation of throughput rate.
    /// </summary>
    public required double StdDevRate { get; init; }

    /// <summary>
    /// Coefficient of variation as percentage (0-100+).
    /// </summary>
    public required double CvRate { get; init; }

    /// <summary>
    /// Average response time in milliseconds (inverse of throughput).
    /// </summary>
    public required double AvgResponseTimeMs { get; init; }

    /// <summary>
    /// Total number of samples.
    /// </summary>
    public required int TotalSamples { get; init; }

    /// <summary>
    /// Total cumulative count (sum of all events/calls).
    /// </summary>
    public required int TotalCount { get; init; }
}
