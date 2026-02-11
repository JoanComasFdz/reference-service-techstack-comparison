using JoanComasFdz.Result;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure;
using PerformanceTester.Infrastructure.Database;
using Serilog.Context;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Phase 0: Service discovery, infrastructure initialization, database/queue clearing.
/// </summary>
internal static class SetupPhase
{
    public static async Task<int> ExecuteAsync(
        TestConfiguration config,
        Guid testRunId,
        IServiceDiscovery serviceDiscovery,
        IHost host,
        IHostApplicationLifetime hostLifetime,
        IEnumerable<IDockerMonitor> dockerMonitors,
        IDatabase database,
        IRabbitMQ rabbitMq,
        IEventPublisher eventPublisher,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var _ = LogContext.PushProperty("TestRunId", testRunId);
        using var __ = LogContext.PushProperty("Phase", "Setup");

        logger.LogInformation(
            "Starting setup phase for service on port {Port} (database: {Database})",
            config.ServicePort,
            config.DatabaseName);

        // Step 1: Find service process
        logger.LogInformation("Discovering service on port {Port}...", config.ServicePort);

        var serviceDiscoveryResult = await serviceDiscovery.FindServiceProcessIdAsync(
            config.ServicePort.Value,
            timeout: TimeSpan.FromSeconds(30),
            cancellationToken);

        var serviceProcessId = serviceDiscoveryResult.Match(
            success: s => s.Value,
            failure: f => throw new TimeoutException(
                $"Service not found on port {config.ServicePort} within 30 seconds. " +
                "Ensure the service is running and listening on the specified port."));

        logger.LogInformation(
            "Service discovered: PID {ProcessId}",
            serviceProcessId);

        // Step 2: Start IHost (all BackgroundServices start, ProcessMonitor waits)
        // Check if host is already started (e.g., by System.CommandLine.Hosting in CLI)
        // IHostApplicationLifetime.ApplicationStarted is cancelled when the host has started
        if (hostLifetime.ApplicationStarted.IsCancellationRequested)
        {
            logger.LogInformation("Host already started, monitoring services are running");
        }
        else
        {
            logger.LogInformation("Starting monitoring services...");
            await host.StartAsync(cancellationToken);
            logger.LogInformation("All monitoring services started");
        }

        // Step 3: Warm up Docker API (first call is slow ~2-3 seconds)
        // We do this in setup so the delay doesn't affect the measured test
        var dockerMonitorsList = dockerMonitors.ToList();
        logger.LogInformation("Warming up Docker API for {Count} monitors: {Names}...",
            dockerMonitorsList.Count,
            string.Join(", ", dockerMonitorsList.Select(m => m.ContainerName)));
        var warmupTasks = dockerMonitorsList.Select(m => m.WarmupAsync(cancellationToken));
        await Task.WhenAll(warmupTasks);
        logger.LogInformation("Docker API warmup complete");

        // Step 4: Clear database
        logger.LogInformation("Clearing database {Database}...", config.DatabaseName);
        (await database.ClearDatabaseAsync(config.DatabaseName.Value, cancellationToken)).Match(
            success: _ => { },
            failure: f => throw new InvalidOperationException($"Failed to clear database '{config.DatabaseName}': {f.Error}"));
        logger.LogInformation("Database cleared");

        // Step 5: Clear RabbitMQ queues
        logger.LogInformation("Clearing RabbitMQ queues...");
        (await rabbitMq.ClearAllQueuesAsync(cancellationToken)).Match(
            success: _ => { },
            failure: f => throw new InvalidOperationException($"Failed to clear RabbitMQ queues: {f.Error}"));
        logger.LogInformation("RabbitMQ queues cleared");

        // Allow time for RabbitMQ consumers to recover after queue purge
        // Queue purging can temporarily disrupt active consumers, this delay ensures they're ready
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

        // Step 6: Connect to RabbitMQ event publisher
        logger.LogInformation("Connecting to RabbitMQ event publisher...");
        await eventPublisher.ConnectAsync(cancellationToken);
        logger.LogInformation("RabbitMQ event publisher connected");

        logger.LogInformation("Setup phase complete");

        return serviceProcessId;
    }
}
