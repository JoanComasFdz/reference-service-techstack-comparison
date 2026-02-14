using JoanComasFdz.Result;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using static JoanComasFdz.Result.Result<JoanComasFdz.Result.Unit, string>;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Teardown phase: disconnect event publisher and stop monitoring services.
/// Runs between API test and reporting. Monitors must be stopped before
/// reporting can collect metrics.
/// Each operation is independently try/caught so a single failure
/// does not prevent the remaining cleanup from running.
/// </summary>
internal static class TeardownPhase
{
    /// <summary>
    /// Disconnects the RabbitMQ event publisher connection.
    /// </summary>
    public delegate Task DisconnectEventPublisher();

    /// <summary>
    /// Stops all monitoring BackgroundServices (process, system, Docker).
    /// </summary>
    public delegate Task StopMonitoring();

    public static async Task<Result<Unit, string>> ExecuteAsync(
        DisconnectEventPublisher disconnectEventPublisher,
        StopMonitoring stopMonitoring,
        ILogger logger)
    {
        using var _ = LogContext.PushProperty("Phase", "Teardown");

        logger.LogInformation("Starting teardown phase");

        var errors = new List<string>();

        // Step 1: Disconnect event publisher
        logger.LogInformation("Disconnecting from RabbitMQ event publisher...");
        try
        {
            await disconnectEventPublisher();
            logger.LogInformation("RabbitMQ event publisher disconnected");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to disconnect event publisher: {Message}", ex.Message);
            errors.Add($"Disconnect event publisher: {ex.Message}");
        }

        // Step 2: Stop monitoring services (critical for reporting phase)
        logger.LogInformation("Stopping monitoring services...");
        try
        {
            await stopMonitoring();
            logger.LogInformation("All monitoring services stopped");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to stop monitoring services: {Message}", ex.Message);
            errors.Add($"Stop monitoring: {ex.Message}");
        }

        if (errors.Count > 0)
        {
            var combined = string.Join("; ", errors);
            logger.LogError("Teardown phase completed with errors: {Errors}", combined);
            return new Failure(combined);
        }

        logger.LogInformation("Teardown phase complete");
        return new Success(Unit.Value);
    }
}
