using Microsoft.Extensions.Logging;
using PerformanceTester.ApiLoadTesting;
using Serilog.Context;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Phase 2: Measured HTTP API load test using k6.
/// </summary>
internal static class ApiTestPhase
{
    public static async Task<(ApiLoadTestResult Result, DateTime StartTime, DateTime EndTime)> ExecuteAsync(
        TestConfiguration config,
        IApiLoadTester apiLoadTester,
        IProgress<PhaseInfo>? progress,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var _ = LogContext.PushProperty("Phase", "ApiTest");

        logger.LogInformation(
            "Starting API load test for {Duration}s with {Workers} worker(s)",
            config.ApiDuration.Value.TotalSeconds,
            config.ApiWorkers);

        var startTime = DateTime.UtcNow;

        // Create explicit API progress callback
        // (CODING_GUIDELINES: Explicit Parameters - callback logic visible here)
        // Use SynchronousProgress to ensure updates happen immediately (not via SynchronizationContext)
        IProgress<ApiLoadProgress>? apiProgress = null;
        if (progress != null)
        {
            apiProgress = new SynchronousProgress<ApiLoadProgress>(info =>
            {
                // Report via PhaseInfo - explicit message format
                progress.Report(PhaseInfo.Starting(
                    TestPhase.ApiTest,
                    $"API: {info.ElapsedSeconds:F1}s/{info.TotalSeconds:F1}s ({info.RequestCount} req)"));
            });
        }

        var result = await apiLoadTester.StartTestAsync(
            config.ApiUrl,
            config.ApiDuration.Value,
            config.ApiWorkers.Value,
            config.MaxConsecutiveApiFailures,
            config.ResultsFolder.Value,
            progress: apiProgress,
            cancellationToken);

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
