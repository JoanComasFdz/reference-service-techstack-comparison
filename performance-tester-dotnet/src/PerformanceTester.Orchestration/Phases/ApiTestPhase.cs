using Microsoft.Extensions.Logging;
using PerformanceTester.ApiLoadTesting;
using Serilog.Context;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Phase 2: Measured HTTP API load test using k6.
/// </summary>
internal static class ApiTestPhase
{
    // TODO: When IApiLoadTester.StartTestAsync is migrated to the Result pattern,
    // update this delegate's return type and ExecuteAsync's signature/return type accordingly.

    /// <summary>
    /// Executes an HTTP API load test using k6 and returns aggregated results.
    /// </summary>
    public delegate Task<ApiLoadTestResult> StartApiLoadTest(
        string targetUrl,
        TimeSpan duration,
        int virtualUsers,
        ReportApiLoadProgress progress,
        int maxConsecutiveFailures,
        string? scriptDirectory);

    public static async Task<(ApiLoadTestResult Result, DateTime StartTime, DateTime EndTime)> ExecuteAsync(
        TestConfiguration config,
        StartApiLoadTest startApiLoadTest,
        IProgress<PhaseInfo>? progress,
        ILogger logger)
    {
        using var _ = LogContext.PushProperty("Phase", "ApiTest");

        logger.LogInformation(
            "Starting API load test for {Duration}s with {Workers} worker(s)",
            config.ApiDuration.Value.TotalSeconds,
            config.ApiWorkers);

        var startTime = DateTime.UtcNow;

        ReportApiLoadProgress apiProgress = progress is null
            ? (_) => { }
            : (info) => progress.Report(PhaseInfo.Starting(TestPhase.ApiTest, $"API: {info.ElapsedSeconds:F1}s/{info.TotalSeconds:F1}s ({info.RequestCount} req)"));

        var result = await startApiLoadTest(
            config.ApiUrl,
            config.ApiDuration.Value,
            config.ApiWorkers.Value,
            apiProgress,
            config.MaxConsecutiveApiFailures,
            config.ResultsFolder.Value
            );

        var endTime = DateTime.UtcNow;

        logger.LogInformation(
            "API load test complete: {Requests} requests in {Duration:F2}s " +
            "({Rate:F2} req/s, {Failures} failures)",
            result.TotalRequests,
            result.TotalDuration.TotalSeconds,
            result.RequestsPerSecond,
            result.FailedRequests);

        logger.LogInformation(
            "API response times: P95={P95:F2}ms, P99={P99:F2}ms",
            result.P95RequestDurationMs,
            result.P99RequestDurationMs);

        return (result, startTime, endTime);
    }
}
