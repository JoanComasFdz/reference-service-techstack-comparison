using PerformanceTester.Functional;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure.Database;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.Orchestration;
using Serilog.Context;
using static PerformanceTester.Functional.Result<PerformanceTester.Infrastructure.ValueObjects.ProcessId, string>;

namespace PerformanceTester.Orchestration.Internal;

/// <summary>
/// Phase 0: Service discovery, infrastructure initialization, database/queue clearing.
/// </summary>
internal static class SetupPhase
{
    /// <summary>
    /// Discovers the service process ID listening on the target port.
    /// Returns the ProcessId on success, or an error message on failure.
    /// </summary>
    public delegate Task<Result<ProcessId, string>> FindServiceProcessIdDelegate();

    /// <summary>
    /// Returns true if monitoring services (BackgroundServices) are already running.
    /// </summary>
    public delegate bool IsMonitoringStartedDelegate();

    /// <summary>
    /// Starts all monitoring services (process, system, Docker).
    /// </summary>
    public delegate Task StartMonitoringDelegate();

    /// <summary>
    /// Warms up Docker API connections to avoid measurement delays.
    /// First Docker API call is typically slow (~2-3s).
    /// </summary>
    public delegate Task WarmupDockerApiDelegate();

    /// <summary>
    /// Establishes connection to RabbitMQ for event publishing.
    /// </summary>
    public delegate Task ConnectEventPublisherDelegate();

    /// <summary>
    /// Bundles all phase-level and shared delegates needed by <see cref="ExecuteAsync"/>.
    /// </summary>
    public record Dependencies(
        FindServiceProcessIdDelegate FindServiceProcessId,
        IsMonitoringStartedDelegate IsMonitoringStarted,
        StartMonitoringDelegate StartMonitoring,
        WarmupDockerApiDelegate WarmupDockerApi,
        SharedPhaseDelegates.ClearDatabaseDelegate ClearDatabase,
        SharedPhaseDelegates.ClearAllQueuesDelegate ClearAllQueues,
        ConnectEventPublisherDelegate ConnectEventPublisher);

    /// <summary>
    /// Resolves DI services and composes phase-level delegates into a <see cref="Dependencies"/> bundle.
    /// </summary>
    public static Dependencies BuildDependencies(
        IServiceProvider services,
        SharedPhaseDelegates.ClearDatabaseDelegate clearDatabase,
        SharedPhaseDelegates.ClearAllQueuesDelegate clearAllQueues,
        TestConfiguration config,
        CancellationToken ct)
    {
        var findServiceProcessId = services.GetRequiredService<PerformanceTester.Infrastructure.FindServiceProcessIdDelegate>();
        var hostLifetime = services.GetRequiredService<IHostApplicationLifetime>();
        var host = services.GetRequiredService<IHost>();
        var warmupDockerMonitors = services.GetRequiredService<WarmupDockerMonitorsDelegate>();
        var connectPublisher = services.GetRequiredService<ConnectPublisherDelegate>();

        return new Dependencies(
            FindServiceProcessId: () => findServiceProcessId(config.ServicePort, TimeSpan.FromSeconds(30), ct),
            IsMonitoringStarted: () => hostLifetime.ApplicationStarted.IsCancellationRequested,
            StartMonitoring: () => host.StartAsync(ct),
            WarmupDockerApi: () => warmupDockerMonitors(ct),
            ClearDatabase: clearDatabase,
            ClearAllQueues: clearAllQueues,
            ConnectEventPublisher: () => connectPublisher(ct));
    }

    public static async Task<Result<ProcessId, string>> ExecuteAsync(
        Guid testRunId,
        Dependencies deps,
        ILogger logger)
    {
        using var _ = LogContext.PushProperty("TestRunId", testRunId);
        using var __ = LogContext.PushProperty("Phase", "Setup");

        logger.LogInformation("Starting setup phase");

        // Step 1: Find service process
        logger.LogInformation("Discovering service process...");
        var pidResult = await deps.FindServiceProcessId();
        if (pidResult.IsFailure)
        {
            return new Failure(pidResult.FailureError);
        }

        var serviceProcessId = pidResult.SuccessValue;
        logger.LogInformation("Service discovered: PID {ProcessId}", serviceProcessId);

        // Step 2: Start monitoring services
        if (deps.IsMonitoringStarted())
        {
            logger.LogInformation("Host already started, monitoring services are running");
        }
        else
        {
            logger.LogInformation("Starting monitoring services...");
            await deps.StartMonitoring();
            logger.LogInformation("All monitoring services started");
        }

        // Step 3: Warm up Docker API
        logger.LogInformation("Warming up Docker API...");
        await deps.WarmupDockerApi();
        logger.LogInformation("Docker API warmup complete");

        // Step 4: Clear database
        logger.LogInformation("Clearing database...");
        var dbResult = await deps.ClearDatabase();
        if (dbResult.IsFailure)
        {
            var errorMessage = dbResult.FailureError.Match(
                databaseNotFound: e => $"Database '{e.Name}' not found",
                retriesExhausted: e => $"All {e.Attempts} retry attempts exhausted: {e.Last.Message}");
            return new Failure($"Failed to clear database: {errorMessage}");
        }

        logger.LogInformation("Database cleared");

        // Step 5: Clear RabbitMQ queues
        logger.LogInformation("Clearing RabbitMQ queues...");
        var queuesResult = await deps.ClearAllQueues();
        if (queuesResult.IsFailure)
        {
            return new Failure($"Failed to clear RabbitMQ queues: {queuesResult.FailureError}");
        }

        logger.LogInformation("RabbitMQ queues cleared");

        // Step 6: Connect event publisher
        logger.LogInformation("Connecting to RabbitMQ event publisher...");
        await deps.ConnectEventPublisher();
        logger.LogInformation("RabbitMQ event publisher connected");

        logger.LogInformation("Setup phase complete");

        return new Success(serviceProcessId);
    }
}
