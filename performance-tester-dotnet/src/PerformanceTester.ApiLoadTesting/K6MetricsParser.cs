using System.Text.Json;
using PerformanceTester.Functional;
using static PerformanceTester.Functional.Result<PerformanceTester.ApiLoadTesting.K6Metric, PerformanceTester.ApiLoadTesting.ParseLineError>;

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
    /// Parses a k6 JSON output line and returns the metric or a typed failure.
    /// </summary>
    /// <param name="jsonLine">JSON line from k6 output.</param>
    /// <returns>Parsed K6Metric on success, or a ParseLineError describing why the line was skipped.</returns>
    public Result<K6Metric, ParseLineError> ParseLine(string jsonLine)
    {
        if (string.IsNullOrWhiteSpace(jsonLine))
        {
            return new Failure(new ParseLineError.EmptyInput());
        }

        try
        {
            var metric = JsonSerializer.Deserialize<K6Metric>(jsonLine, _jsonOptions);

            if (metric?.Type != "Point")
            {
                return new Failure(
                    new ParseLineError.NonPointMetric(metric?.Type ?? "null"));
            }

            if (metric.Metric == null || !IsRelevantMetric(metric.Metric))
            {
                return new Failure(
                    new ParseLineError.IrrelevantMetric(metric.Metric ?? "null"));
            }

            return new Success(metric);
        }
        catch (JsonException)
        {
            return new Failure(
                new ParseLineError.InvalidJson(jsonLine));
        }
    }

    /// <summary>
    /// Determines if a metric name is relevant for our analysis.
    /// </summary>
    private static bool IsRelevantMetric(string metricName) => metricName switch
        {
            "http_reqs" => true,
            "http_req_duration" => true,
            "http_req_failed" => true,
            "vus" => true,
            _ => false
        };
}
