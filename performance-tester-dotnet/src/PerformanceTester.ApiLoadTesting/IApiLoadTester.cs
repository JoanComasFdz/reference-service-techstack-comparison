namespace PerformanceTester.ApiLoadTesting;

/// <summary>
/// Service for running API load tests using k6.
/// Executes k6 process and returns aggregated metrics when test completes.
/// </summary>
public interface IApiLoadTester
{
    /// <summary>
    /// Starts an API load test against the specified endpoint and returns the result when complete.
    /// </summary>
    /// <param name="targetUrl">Target endpoint URL to test (e.g., "http://localhost:8094/kpi").</param>
    /// <param name="duration">Test duration (e.g., TimeSpan.FromSeconds(30)).</param>
    /// <param name="virtualUsers">Number of virtual users (concurrent requests).</param>
    /// <param name="maxConsecutiveFailures">Maximum consecutive failures before aborting (0 = disabled, default: 3).</param>
    /// <param name="scriptDirectory">Directory for temporary k6 scripts. If null, uses user's home directory.
    /// Note: /tmp is avoided as it's not accessible to snap-installed k6.</param>
    /// <param name="reportApiLoadProgress">Progress reporter for real-time updates.</param>
    /// <param name="cancellationToken">Cancellation token to stop the test early.</param>
    /// <returns>API load test result with aggregated metrics.</returns>
    /// <exception cref="ArgumentException">Invalid target URL or parameters.</exception>
    /// <exception cref="InvalidOperationException">k6 binary not found or execution failed.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    Task<ApiLoadTestResult> StartTestAsync(
        string targetUrl,
        TimeSpan duration,
        int virtualUsers,
        ReportApiLoadProgressDelegate reportApiLoadProgress,
        int maxConsecutiveFailures = 3,
        string? scriptDirectory = null,
        CancellationToken cancellationToken = default);
}
