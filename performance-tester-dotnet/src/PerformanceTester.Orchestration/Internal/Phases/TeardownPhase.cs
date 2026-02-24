using PerformanceTester.Functional;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.EventPublishing;
using Serilog.Context;
using static PerformanceTester.Functional.Result<PerformanceTester.Functional.Unit, string>;

namespace PerformanceTester.Orchestration.Internal;

/// <summary>
/// Teardown phase: disconnect event publisher and stop monitoring services.
/// Runs between API test and reporting. Monitors must be stopped before
/// reporting can collect metrics.
/// Each operation is independently try/caught so a single failure
/// does not prevent the remaining cleanup from running.
/// </summary>
internal static class TeardownPhase
{
    // -- Delegate definitions (what I need) ----------------------------------------

    /// <summary>
    /// Disconnects the RabbitMQ event publisher connection.
    /// </summary>
    public delegate Task DisconnectEventPublisherDelegate(CancellationToken ct);

    /// <summary>
    /// Stops all monitoring BackgroundServices (process, system, Docker).
    /// </summary>
    public delegate Task StopMonitoringDelegate(CancellationToken ct);

    // -- Dependencies record (bundle of what I need) -------------------------------

    public record Dependencies(
        DisconnectEventPublisherDelegate DisconnectEventPublisher,
        StopMonitoringDelegate StopMonitoring);

    // -- Factory (how to build what I need from DI) --------------------------------

    public static Dependencies BuildDependencies(IServiceProvider services, CancellationToken ct)
    {
        var disconnectPublisher = services.GetRequiredService<DisconnectPublisherDelegate>();
        var host = services.GetRequiredService<IHost>();

        return new Dependencies(
            DisconnectEventPublisher: (ct) => disconnectPublisher(ct),
            StopMonitoring: (ct) => host.StopAsync(ct));
    }

    // -- Execution (what I do with it) ---------------------------------------------

    public static async Task<Result<Unit, string>> ExecuteAsync(
        Dependencies deps,
        CancellationToken ct, // Teardown can be done via normal operation (with cancellation) or as part of a forced cleanup after cancellation (requireing a different CT, normally none)
        ILogger logger)
    {
        using var _ = LogContext.PushProperty("Phase", "Teardown");

        logger.LogInformation("Starting teardown phase");

        var errors = new List<string>();

        // Step 1: Disconnect event publisher
        logger.LogInformation("Disconnecting from RabbitMQ event publisher...");
        try
        {
            await deps.DisconnectEventPublisher(ct);
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
            await deps.StopMonitoring(ct);
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
