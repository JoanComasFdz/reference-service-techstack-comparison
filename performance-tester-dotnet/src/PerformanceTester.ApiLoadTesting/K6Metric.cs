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
    [JsonPropertyName("time")]
    public DateTime Time { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}
