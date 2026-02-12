using JoanComasFdz.Result;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.Database;
using Serilog.Context;
using static JoanComasFdz.Result.Result<int, string>;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Phase 0: Service discovery, infrastructure initialization, database/queue clearing.
/// </summary>
internal static class SetupPhase
{
    /// <summary>
    /// Discovers the service process ID listening on the target port.
    /// Returns the PID on success, or an error message on failure.
    /// </summary>
    public delegate Task<Result<int, string>> FindServiceProcessId();

    /// <summary>
    /// Returns true if monitoring services (BackgroundServices) are already running.
    /// </summary>
    public delegate bool IsMonitoringStarted();

    /// <summary>
    /// Starts all monitoring services (process, system, Docker).
    /// </summary>
    public delegate Task StartMonitoring();

    /// <summary>
    /// Warms up Docker API connections to avoid measurement delays.
    /// First Docker API call is typically slow (~2-3s).
    /// </summary>
    public delegate Task WarmupDockerApi();

    /// <summary>
    /// Clears all data from the target database.
    /// Returns Unit on success, or a ClearDatabaseError on failure.
    /// </summary>
    public delegate Task<Result<Unit, ClearDatabaseError>> ClearDatabase();

    /// <summary>
    /// Purges all RabbitMQ queues and waits for consumer recovery.
    /// Returns Unit on success, or an error message on failure.
    /// </summary>
    public delegate Task<Result<Unit, string>> ClearAllQueues();

    /// <summary>
    /// Establishes connection to RabbitMQ for event publishing.
    /// </summary>
    public delegate Task ConnectEventPublisher();

    public static async Task<Result<int, string>> ExecuteAsync(
        Guid testRunId,
        FindServiceProcessId findServiceProcessId,
        IsMonitoringStarted isMonitoringStarted,
        StartMonitoring startMonitoring,
        WarmupDockerApi warmupDockerApi,
        ClearDatabase clearDatabase,
        ClearAllQueues clearAllQueues,
        ConnectEventPublisher connectEventPublisher,
        ILogger logger)
    {
        using var _ = LogContext.PushProperty("TestRunId", testRunId);
        using var __ = LogContext.PushProperty("Phase", "Setup");

        logger.LogInformation("Starting setup phase");

        // Step 1: Find service process
        logger.LogInformation("Discovering service process...");
        var pidResult = await findServiceProcessId();
        if (pidResult.IsFailure)
            return new Failure(pidResult.FailureError);
        var serviceProcessId = pidResult.SuccessValue;
        logger.LogInformation("Service discovered: PID {ProcessId}", serviceProcessId);

        // Step 2: Start monitoring services
        if (isMonitoringStarted())
        {
            logger.LogInformation("Host already started, monitoring services are running");
        }
        else
        {
            logger.LogInformation("Starting monitoring services...");
            await startMonitoring();
            logger.LogInformation("All monitoring services started");
        }

        // Step 3: Warm up Docker API
        logger.LogInformation("Warming up Docker API...");
        await warmupDockerApi();
        logger.LogInformation("Docker API warmup complete");

        // Step 4: Clear database
        logger.LogInformation("Clearing database...");
        var dbResult = await clearDatabase();
        if (dbResult.IsFailure)
        {
            var errorMessage = dbResult.FailureError.Match(
                emptyName: _ => "Database name was empty",
                databaseNotFound: e => $"Database '{e.Name}' not found",
                retriesExhausted: e => $"All {e.Attempts} retry attempts exhausted: {e.Last.Message}");
            return new Failure($"Failed to clear database: {errorMessage}");
        }
        logger.LogInformation("Database cleared");

        // Step 5: Clear RabbitMQ queues
        logger.LogInformation("Clearing RabbitMQ queues...");
        var queuesResult = await clearAllQueues();
        if (queuesResult.IsFailure)
            return new Failure($"Failed to clear RabbitMQ queues: {queuesResult.FailureError}");
        logger.LogInformation("RabbitMQ queues cleared");

        // Step 6: Connect event publisher
        logger.LogInformation("Connecting to RabbitMQ event publisher...");
        await connectEventPublisher();
        logger.LogInformation("RabbitMQ event publisher connected");

        logger.LogInformation("Setup phase complete");

        return new Success(serviceProcessId);
    }
}
