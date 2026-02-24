using PerformanceTester.Functional;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.ApiLoadTesting;
using PerformanceTester.Orchestration.ValueObjects;
using PerformanceTester.Reporting.ValueObjects;
using PerformanceTester.Orchestration;
using Serilog.Context;
using static PerformanceTester.Functional.Result<PerformanceTester.Orchestration.Internal.ApiTestPhaseModule.Output, string>;

namespace PerformanceTester.Orchestration.Internal;

/// <summary>
/// Phase 2: Measured HTTP API load test using k6.
/// </summary>
internal static class ApiTestPhaseModule
{
    // -- Delegate definitions (what I need) ----------------------------------------

    /// <summary>
    /// Executes an HTTP API load test using k6 and returns aggregated results.
    /// </summary>
    public delegate Task<ApiLoadTestResult> StartApiLoadTestDelegate(
        ServiceUrl targetUrl,
        ApiDuration duration,
        WorkerCount virtualWorkers,
        MaxConsecutiveFailures maxConsecutiveFailures,
        ResultsOutputFolder resultsFolder);

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
    public record Dependencies(StartApiLoadTestDelegate StartApiLoadTest);

    // -- Factory (how to build what I need from DI) --------------------------------

    /// <summary>
    /// Resolves DI services and composes phase-level delegates into a <see cref="Dependencies"/> bundle.
    /// </summary>
    public static Dependencies BuildDependencies(
        IServiceProvider services,
        ReportPhaseProgressDelegate reportProgress,
        CancellationToken ct)
    {
        var apiLoadTester = services.GetRequiredService<IApiLoadTester>();

        ReportApiLoadProgressDelegate reportApiProgress = (info) =>
            reportProgress(PhaseInfo.Starting(TestPhase.ApiTest, $"API: {info.ElapsedSeconds:F1}s/{info.TotalSeconds:F1}s ({info.RequestCount} req)"));

        return new Dependencies(
            StartApiLoadTest: (url, duration, vus, maxFail, dir) => apiLoadTester.StartTestAsync(url.Value, duration.Value, vus.Value, reportApiProgress, maxFail.Value, dir.Value, ct)
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
                config.ApiDuration,
                config.ApiWorkers,
                config.MaxConsecutiveApiFailures,
                config.ResultsFolder
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
