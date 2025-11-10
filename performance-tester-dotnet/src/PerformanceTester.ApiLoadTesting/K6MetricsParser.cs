using System.Text.Json;

namespace PerformanceTester.ApiLoadTesting;

/// <summary>
/// Parses k6 JSON output and extracts relevant metrics.
/// Handles metric types: http_reqs, http_req_duration, http_req_failed, vus.
/// </summary>
internal sealed class K6MetricsParser
{
    private readonly JsonSerializerOptions _jsonOptions;

    public K6MetricsParser()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Parses a k6 JSON output line and returns the metric.
    /// Returns null if line is not a valid metric or is not relevant.
    /// </summary>
    /// <param name="jsonLine">JSON line from k6 output.</param>
    /// <returns>Parsed K6Metric or null.</returns>
    public K6Metric? ParseLine(string jsonLine)
    {
        if (string.IsNullOrWhiteSpace(jsonLine))
        {
            return null;
        }

        try
        {
            var metric = JsonSerializer.Deserialize<K6Metric>(jsonLine, _jsonOptions);

            // Only return metrics we care about
            if (metric?.Type == "Point" && metric.Metric != null)
            {
                return IsRelevantMetric(metric.Metric) ? metric : null;
            }

            return null;
        }
        catch (JsonException)
        {
            // Invalid JSON line, ignore
            return null;
        }
    }

    /// <summary>
    /// Determines if a metric name is relevant for our analysis.
    /// </summary>
    private static bool IsRelevantMetric(string metricName) =>
        metricName switch
        {
            "http_reqs" => true,
            "http_req_duration" => true,
            "http_req_failed" => true,
            "vus" => true,
            _ => false
        };
}
