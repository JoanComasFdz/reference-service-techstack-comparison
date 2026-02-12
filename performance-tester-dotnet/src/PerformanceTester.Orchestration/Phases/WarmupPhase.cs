using JoanComasFdz.Result;
using Microsoft.Extensions.Logging;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure.Database;
using Serilog.Context;
using static JoanComasFdz.Result.Result<JoanComasFdz.Result.Unit, string>;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Phase 0.5: Non-measured warmup events and API calls. Failures abort the test.
/// </summary>
internal static class WarmupPhase
{
    /// <summary>
    /// Executes warmup HTTP calls to the API endpoint.
    /// Returns (successCount, failedCount).
    /// </summary>
    public delegate Task<(int Success, int Failed)> ExecuteWarmupApiCalls(string apiUrl, uint callCount);

    public static async Task<Result<Unit, string>> ExecuteAsync(
        TestConfiguration config,
        PhasesToolbox.TrackEvents trackEvents,
        PhasesToolbox.PublishEvents publishEvents,
        ExecuteWarmupApiCalls executeWarmupApiCalls,
        PhasesToolbox.ClearDatabase clearDatabase,
        PhasesToolbox.ClearAllQueues clearAllQueues,
        ILogger logger)
    {
        using var _ = LogContext.PushProperty("Phase", "Warmup");

        logger.LogInformation("Starting warmup phase (not measured)");

        try
        {
            logger.LogInformation(
                "Warmup: Publishing and consuming {Count} events (timeout: {Timeout}s)",
                config.WarmupEventCount,
                config.WarmupInactivityTimeout.Value.TotalSeconds);

            // Start consumer tracking (no progress reporting — warmup is a quick non-measured step)
            var consumerTask = trackEvents(
                config.WarmupEventCount.Value,
                config.WarmupInactivityTimeout.Value,
                progress: null);

            // Publish warmup events
            var publishMetrics = await publishEvents(config.WarmupEventCount.Value);

            logger.LogInformation(
                "Warmup: Published {Count} events in {Duration:F2}s ({Rate:F2} events/s)",
                publishMetrics.EventCount,
                publishMetrics.Duration.TotalSeconds,
                publishMetrics.EventsPerSecond);

            // Wait for consumption
            await consumerTask;

            logger.LogInformation("Warmup: All {Count} events consumed", config.WarmupEventCount);

            // Warmup API using simple HttpClient (not k6)
            logger.LogInformation(
                "Warmup: Making {Count} HTTP calls to API endpoint",
                config.WarmupApiCallCount);

            var (successCount, failCount) = await executeWarmupApiCalls(
                config.ApiUrl, config.WarmupApiCallCount.Value);

            logger.LogInformation(
                "Warmup: API calls complete - {Success} succeeded, {Failed} failed",
                successCount,
                failCount);

            // Clear database and queues again
            logger.LogInformation("Warmup: Clearing database and queues before measured test");
            var dbResult = await clearDatabase();
            if (dbResult.IsFailure)
            {
                var errorMessage = dbResult.FailureError.Match(
                    emptyName: _ => "Database name was empty",
                    databaseNotFound: e => $"Database '{e.Name}' not found",
                    retriesExhausted: e => $"All {e.Attempts} retry attempts exhausted: {e.Last.Message}");
                return new Failure($"Failed to clear database '{config.DatabaseName}' during warmup: {errorMessage}");
            }

            var queuesResult = await clearAllQueues();
            if (queuesResult.IsFailure)
                return new Failure($"Failed to clear RabbitMQ queues during warmup: {queuesResult.FailureError}");

            logger.LogInformation("Warmup phase complete - starting measured test");

            return new Success(Unit.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Warmup phase failed, aborting test. " +
                "Fix the warmup configuration or service availability before running the measured test.");
            return new Failure(ex.Message);
        }
    }

    /// <summary>
    /// Executes warmup API calls using a simple HttpClient.
    /// This method can be passed as the <see cref="ExecuteWarmupApiCalls"/> delegate.
    /// </summary>
    public static async Task<(int Success, int Failed)> ExecuteWarmupApiCallsAsync(
        string apiUrl,
        uint callCount,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        var successCount = 0;
        var failCount = 0;

        for (uint i = 0; i < callCount; i++)
        {
            try
            {
                var response = await httpClient.GetAsync(apiUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    successCount++;
                }
                else
                {
                    failCount++;
                    logger.LogWarning(
                        "Warmup API call {Index} failed with status {StatusCode}",
                        i + 1,
                        response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                failCount++;
                logger.LogWarning(
                    ex,
                    "Warmup API call {Index} failed with exception",
                    i + 1);
            }
        }

        return (successCount, failCount);
    }
}
