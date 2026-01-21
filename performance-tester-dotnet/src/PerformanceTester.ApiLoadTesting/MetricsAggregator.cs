using System.Collections.Concurrent;

namespace PerformanceTester.ApiLoadTesting;

/// <summary>
/// Aggregates k6 metrics into final result.
/// Calculates percentiles, averages, and throughput from raw metrics.
/// </summary>
internal sealed class MetricsAggregator
{
    private readonly ConcurrentBag<ApiThroughputSample> _throughputSamples = new();
    private int _totalRequests;
    private int _failedRequests;
    private readonly List<double> _requestDurations = new();

    /// <summary>
    /// Processes a single k6 metric.
    /// </summary>
    public void ProcessMetric(K6Metric metric)
    {
        if (metric.Data == null) return;

        switch (metric.Metric)
        {
            case "http_reqs":
                // k6 emits one metric per request with value=1 - count them
                _totalRequests += (int)metric.Data.Value;
                break;

            case "http_req_failed":
                // Count failed requests (value is 1 for failure, 0 for success)
                if (metric.Data.Value > 0)
                {
                    _failedRequests++;
                }
                break;

            case "http_req_duration":
                _requestDurations.Add(metric.Data.Value);
                break;

            case "vus":
                // k6 outputs ISO 8601 timestamps - DateTimeOffset preserves timezone correctly
                var sample = new ApiThroughputSample(
                    Timestamp: metric.Data.Time.ToUniversalTime(),
                    RequestsPerSecond: 0,  // Calculated in final result based on actual test duration
                    CumulativeRequestCount: _totalRequests,
                    ActiveVirtualUsers: (int)metric.Data.Value);
                _throughputSamples.Add(sample);
                break;
        }
    }

    /// <summary>
    /// Computes final aggregated result.
    /// </summary>
    /// <param name="actualDuration">The actual duration of the test (from k6 execution start to end).</param>
    public ApiLoadTestResult ComputeResult(TimeSpan actualDuration)
    {
        var avgDuration = _requestDurations.Count > 0 ? _requestDurations.Average() : 0;
        var p95Duration = CalculatePercentile(_requestDurations, 0.95);
        var p99Duration = CalculatePercentile(_requestDurations, 0.99);
        var requestsPerSecond = actualDuration.TotalSeconds > 0
            ? _totalRequests / actualDuration.TotalSeconds
            : 0;

        return new ApiLoadTestResult(
            TotalRequests: _totalRequests,
            TotalDuration: actualDuration,
            FailedRequests: _failedRequests,
            AverageRequestDurationMs: Math.Round(avgDuration, 2),
            P95RequestDurationMs: Math.Round(p95Duration, 2),
            P99RequestDurationMs: Math.Round(p99Duration, 2),
            RequestsPerSecond: Math.Round(requestsPerSecond, 2),
            // ConcurrentBag doesn't preserve insertion order, so sort by timestamp
            ThroughputSamples: _throughputSamples.OrderBy(s => s.Timestamp).ToArray());
    }

    private static double CalculatePercentile(List<double> values, double percentile)
    {
        if (values.Count == 0) return 0;

        var sorted = values.OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling(sorted.Count * percentile) - 1;
        index = Math.Max(0, Math.Min(sorted.Count - 1, index));
        return sorted[index];
    }
}
