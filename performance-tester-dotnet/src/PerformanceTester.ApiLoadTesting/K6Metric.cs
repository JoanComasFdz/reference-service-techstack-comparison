using System.Text.Json.Serialization;

namespace PerformanceTester.ApiLoadTesting;

/// <summary>
/// Represents a k6 JSON metric output line.
/// Maps to k6's JSON output format.
/// </summary>
internal sealed class K6Metric
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("metric")]
    public string Metric { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public K6MetricData? Data { get; set; }
}

/// <summary>
/// Data portion of k6 metric.
/// </summary>
internal sealed class K6MetricData
{
    /// <summary>
    /// k6 outputs ISO 8601 timestamps (e.g., "2026-01-21T15:43:05.123Z").
    /// Using DateTimeOffset preserves timezone information correctly.
    /// </summary>
    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}
