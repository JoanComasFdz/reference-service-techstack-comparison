using PerformanceTester.Functional;
using Microsoft.Extensions.Logging;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure.Database;
using PerformanceTester.Orchestration.ValueObjects;
using Serilog.Context;
using static PerformanceTester.Functional.Result<PerformanceTester.Functional.Unit, string>;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Phase 0.5: Non-measured warmup events and API calls. Failures abort the test.
/// </summary>
internal static class WarmupPhase
{
    /// <summary>
    /// Bundles all phase-level and shared delegates needed by <see cref="ExecuteAsync"/>.
    /// </summary>
    public record Dependencies(
        PhasesToolbox.TrackEventsDelegate TrackEvents,
        PhasesToolbox.PublishEventsDelegate PublishEvents,
        PhasesToolbox.ClearDatabaseDelegate ClearDatabase,
        PhasesToolbox.ClearAllQueuesDelegate ClearAllQueues);

    /// <summary>
    /// Composes phase-level delegates into a <see cref="Dependencies"/> bundle.
    /// Takes <paramref name="logger"/> because the <see cref="ExecuteWarmupApiCalls"/>
    /// closure captures it for HTTP call logging.
    /// </summary>
    public static Dependencies BuildDependencies(
        PhasesToolbox.TrackEventsDelegate trackEvents,
        PhasesToolbox.PublishEventsDelegate publishEvents,
        PhasesToolbox.ClearDatabaseDelegate clearDatabase,
        PhasesToolbox.ClearAllQueuesDelegate clearAllQueues)
    {
        return new Dependencies(
            TrackEvents: trackEvents,
            PublishEvents: publishEvents,
            ClearDatabase: clearDatabase,
            ClearAllQueues: clearAllQueues);
    }

    public static async Task<Result<Unit, string>> ExecuteAsync(
        TestConfiguration config,
        Dependencies deps,
        ILogger logger,
        CancellationToken ct)
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
            var consumerTask = deps.TrackEvents(
                config.WarmupEventCount.Value,
                config.WarmupInactivityTimeout.Value,
                progress: null);

            // Publish warmup events
            var publishMetrics = await deps.PublishEvents(config.WarmupEventCount.Value);

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

            var (successCount, failCount) = await ExecuteWarmupApiCallsAsync(config.ApiUrl, config.WarmupApiCallCount, logger, ct);

            logger.LogInformation(
                "Warmup: API calls complete - {Success} succeeded, {Failed} failed",
                successCount,
                failCount);

            // Clear database and queues again
            logger.LogInformation("Warmup: Clearing database and queues before measured test");
            var dbResult = await deps.ClearDatabase();
            if (dbResult.IsFailure)
            {
                var errorMessage = dbResult.FailureError.Match(
                    databaseNotFound: e => $"Database '{e.Name}' not found",
                    retriesExhausted: e => $"All {e.Attempts} retry attempts exhausted: {e.Last.Message}");
                return new Failure($"Failed to clear database '{config.DatabaseName}' during warmup: {errorMessage}");
            }

            var queuesResult = await deps.ClearAllQueues();
            if (queuesResult.IsFailure)
            {
                return new Failure($"Failed to clear RabbitMQ queues during warmup: {queuesResult.FailureError}");
            }

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
    private static async Task<(int Success, int Failed)> ExecuteWarmupApiCallsAsync(
        string apiUrl,
        WarmupApiCallsCount callCount,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        var successCount = 0;
        var failCount = 0;

        for (uint i = 0; i < callCount.Value; i++)
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
