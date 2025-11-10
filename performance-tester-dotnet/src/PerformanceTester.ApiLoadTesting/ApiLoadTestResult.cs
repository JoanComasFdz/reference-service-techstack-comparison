namespace PerformanceTester.ApiLoadTesting;

/// <summary>
/// Represents the final result of an API load test.
/// This model is owned by the ApiLoadTesting slice (producer-owned contract).
/// </summary>
/// <param name="TotalRequests">Total HTTP requests sent during test.</param>
/// <param name="TotalDuration">Total test duration.</param>
/// <param name="FailedRequests">Number of failed HTTP requests.</param>
/// <param name="AverageRequestDurationMs">Average request duration in milliseconds.</param>
/// <param name="P95RequestDurationMs">95th percentile request duration in milliseconds.</param>
/// <param name="P99RequestDurationMs">99th percentile request duration in milliseconds.</param>
/// <param name="RequestsPerSecond">Average requests per second (throughput).</param>
/// <param name="ThroughputSamples">Collection of throughput samples captured during test.</param>
public record ApiLoadTestResult(
    int TotalRequests,
    TimeSpan TotalDuration,
    int FailedRequests,
    double AverageRequestDurationMs,
    double P95RequestDurationMs,
    double P99RequestDurationMs,
    double RequestsPerSecond,
    IReadOnlyCollection<ApiThroughputSample> ThroughputSamples);
