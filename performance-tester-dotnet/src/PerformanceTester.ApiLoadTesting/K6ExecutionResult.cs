namespace PerformanceTester.ApiLoadTesting;

/// <summary>
/// Represents the result of a k6 execution, including metrics and abort information.
/// </summary>
/// <param name="Metrics">Collection of parsed k6 metrics.</param>
/// <param name="WasAborted">True if the test was aborted early (e.g., due to consecutive failures).</param>
/// <param name="AbortReason">Reason for abort, extracted from k6 output.</param>
internal record K6ExecutionResult(
    List<K6Metric> Metrics,
    bool WasAborted = false,
    string? AbortReason = null);
