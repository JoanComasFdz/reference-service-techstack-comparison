using JoanComasFdz.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.ApiLoadTesting;
using Serilog.Context;
using static JoanComasFdz.Result.Result<PerformanceTester.Orchestration.ApiTestPhase.Output, string>;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Phase 2: Measured HTTP API load test using k6.
/// </summary>
internal static class ApiTestPhase
{
    // -- Delegate definitions (what I need) ----------------------------------------

    /// <summary>
    /// Executes an HTTP API load test using k6 and returns aggregated results.
    /// </summary>
    public delegate Task<ApiLoadTestResult> StartApiLoadTest(
        string targetUrl,
        TimeSpan duration,
        int virtualUsers,
        int maxConsecutiveFailures,
        string? scriptDirectory);

    // -- Output (what I produce) --------------------------------------------------

    /// <summary>
    /// Success output of the API test phase.
    /// </summary>
    public sealed record Output(
        ApiLoadTestResult ApiLoadTestResult,
        DateTime StartTime,
        DateTime EndTime);

    // -- Dependencies record (bundle of what I need) ------------------------------

    /// <summary>
    /// Pre-bound dependencies for the API test phase.
    /// </summary>
    public record Dependencies(StartApiLoadTest StartApiLoadTest);

    // -- Factory (how to build what I need from DI) --------------------------------

    /// <summary>
    /// Resolves DI services and composes phase-level delegates into a <see cref="Dependencies"/> bundle.
    /// </summary>
    public static Dependencies BuildDependencies(
        IServiceProvider services,
        IProgress<PhaseInfo>? progress,
        CancellationToken ct)
    {
        var apiLoadTester = services.GetRequiredService<IApiLoadTester>();

        ReportApiLoadProgress reportProgress = progress is null
            ? (_) => { }
            : (info) => progress.Report(PhaseInfo.Starting(TestPhase.ApiTest, $"API: {info.ElapsedSeconds:F1}s/{info.TotalSeconds:F1}s ({info.RequestCount} req)"));

        return new Dependencies(
            StartApiLoadTest: (url, duration, vus, maxFail, dir) => apiLoadTester.StartTestAsync(url, duration, vus, reportProgress, maxFail, dir, ct)
            );
    }

    // -- Execution (what I do with it) --------------------------------------------

    public static async Task<Result<Output, string>> ExecuteAsync(
        TestConfiguration config,
        Dependencies deps,
        ILogger logger)
    {
        using var _ = LogContext.PushProperty("Phase", "ApiTest");

        try
        {
            logger.LogInformation(
                "Starting API load test for {Duration}s with {Workers} worker(s)",
                config.ApiDuration.Value.TotalSeconds,
                config.ApiWorkers);

            var startTime = DateTime.UtcNow;

            var result = await deps.StartApiLoadTest(
                config.ApiUrl,
                config.ApiDuration.Value,
                config.ApiWorkers.Value,
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

            return new Success(new Output(result, startTime, endTime));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "API test phase failed: {Message}", ex.Message);
            return new Failure(ex.Message);
        }
    }
}
