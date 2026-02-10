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
    public required DateTime TestDate { get; init; }

    /// <summary>
    /// Sampling interval in milliseconds.
    /// </summary>
    public required int SamplingIntervalMs { get; init; }

    /// <summary>
    /// Individual throughput samples (time-series data).
    /// </summary>
    public required IReadOnlyList<ThroughputSampleJson> Samples { get; init; }

    /// <summary>
    /// Statistical summary of throughput data (generic/normalized names).
    /// Nullable because CompareCommand doesn't need summaries (it computes its own from samples).
    /// </summary>
    public ThroughputSummary? Summary { get; init; }
}

/// <summary>
/// Individual throughput sample for JSON input/output.
/// Used for chart generation - stores normalized values.
/// </summary>
public sealed record ThroughputSampleJson
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
    /// Cumulative count (total events or calls) - normalized from source data.
    /// Property order matches Python JSON format (total_events before events_per_second).
    /// </summary>
    public int TotalEvents { get; init; }

    /// <summary>
    /// Throughput rate (events/s or calls/s) - normalized from source data.
    /// </summary>
    public double EventsPerSecond { get; init; }

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
/// Used for internal calculation output and chart data loading.
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

/// <summary>
/// Events throughput report for JSON serialization.
/// </summary>
public sealed record EventsThroughputReportJson
{
    [JsonPropertyName("test_date")]
    public required string TestDateFormatted { get; init; }

    public required int SamplingIntervalMs { get; init; }

    public required IReadOnlyList<ThroughputSampleJson> Samples { get; init; }

    public required EventsThroughputSummaryJson Summary { get; init; }
}

/// <summary>
/// Events throughput summary for JSON serialization.
/// Property names serialize to events-specific snake_case via SnakeCaseLower policy
/// (e.g., AvgEventsPerSecond → avg_events_per_second).
/// </summary>
public sealed record EventsThroughputSummaryJson
{
    public required double AvgEventsPerSecond { get; init; }
    public required double PeakEventsPerSecond { get; init; }
    public required double MinEventsPerSecond { get; init; }
    public required double StdDevEventsPerSecond { get; init; }
    public required double CvEventsPerSecond { get; init; }
    public required double AvgResponseTimeMs { get; init; }
    public required int TotalSamples { get; init; }
    public required int TotalEvents { get; init; }
}

/// <summary>
/// API throughput report for JSON serialization.
/// </summary>
public sealed record ApiThroughputReportJson
{
    [JsonPropertyName("test_date")]
    public required string TestDateFormatted { get; init; }

    public required int SamplingIntervalMs { get; init; }

    public required IReadOnlyList<ApiThroughputSampleJson> Samples { get; init; }

    public required ApiThroughputSummaryJson Summary { get; init; }
}

/// <summary>
/// API throughput sample for JSON serialization.
/// Uses API-specific naming (calls instead of events).
/// </summary>
public sealed record ApiThroughputSampleJson
{
    public required DateTime Timestamp { get; init; }
    public required double ElapsedSeconds { get; init; }
    public required int TotalCalls { get; init; }
    public required double CallsPerSecond { get; init; }
}

/// <summary>
/// API throughput summary for JSON serialization.
/// Property names serialize to API-specific snake_case via SnakeCaseLower policy
/// (e.g., AvgCallsPerSecond → avg_calls_per_second).
/// </summary>
public sealed record ApiThroughputSummaryJson
{
    public required double AvgCallsPerSecond { get; init; }
    public required double PeakCallsPerSecond { get; init; }
    public required double MinCallsPerSecond { get; init; }
    public required double StdDevCallsPerSecond { get; init; }
    public required double CvCallsPerSecond { get; init; }
    public required double AvgResponseTimeMs { get; init; }
    public required int TotalSamples { get; init; }
    public required int TotalCalls { get; init; }
}
