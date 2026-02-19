using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring.Connection;
using PerformanceTester.DockerMonitoring.Stats;
using PerformanceTester.DockerMonitoring.ValueObjects;

namespace PerformanceTester.DockerMonitoring.Monitoring;

/// <summary>
/// Pure static functions for container monitoring logic.
/// All functions take MonitorContext and delegates as explicit parameters — no instance state.
/// Testable without BackgroundService lifecycle.
/// </summary>
internal static class MonitoringOperations
{
    /// <summary>
    /// State machine interpreter loop.
    /// Pure <see cref="ConnectionStateMachine.Transition"/> decides state changes;
    /// this method interprets states as effects.
    /// </summary>
    public static async Task RunStreamingLoopAsync(
        MonitorContext ctx,
        ContainerId initialContainerId,
        GetContainerIdDelegate getContainerId,
        InvalidateContainerCacheDelegate invalidateCache,
        StreamMetricsDelegate streamMetrics,
        ReportDockerMonitorProgress progress,
        ILogger logger,
        CancellationToken ct)
    {
        ConnectionState state = new ConnectionState.Connecting(
            initialContainerId,
            AttemptCount.FromInt(0),
            StreamingConstants.InitialReconnectDelay);

        while (state is not ConnectionState.Failed && !ct.IsCancellationRequested)
        {
            progress(PhaseReporting.ToPhaseInfo(state, ctx.ContainerName));

            state = state switch
            {
                ConnectionState.Connecting c => await ExecuteStreamingSessionAsync(ctx, c, invalidateCache, streamMetrics, progress, logger, ct),
                ConnectionState.Disconnected d => await ExecuteReconnectAsync(ctx, d, getContainerId, invalidateCache, logger, ct),
                _ => state
            };
        }

        if (state is ConnectionState.Failed)
        {
            progress(PhaseReporting.ToPhaseInfo(state, ctx.ContainerName));

            ctx.StreamingFailed = true;

            logger.LogError(
                "Streaming failed permanently for container {ContainerName}",
                ctx.ContainerName);
        }
    }

    /// <summary>
    /// Starts a streaming session: iterates the IAsyncEnumerable metrics stream,
    /// signals connection success on first valid item, collects all metrics.
    /// Returns the next state based on how the stream ended.
    /// </summary>
    private static async Task<ConnectionState> ExecuteStreamingSessionAsync(
        MonitorContext ctx,
        ConnectionState.Connecting connectingState,
        InvalidateContainerCacheDelegate invalidateCache,
        StreamMetricsDelegate streamMetrics,
        ReportDockerMonitorProgress progress,
        ILogger logger,
        CancellationToken ct)
    {
        ConnectionState currentState = connectingState;

        try
        {
            await foreach (var metrics in streamMetrics(connectingState.ContainerId.Value, ctx.ContainerName, ct))
            {
                if (currentState is ConnectionState.Connecting)
                {
                    currentState = ConnectionStateMachine.Transition(
                        currentState,
                        new StreamEvent.StatsReceived(),
                        StreamingConstants.MaxReconnectAttempts,
                        StreamingConstants.MaxReconnectDelay);

                    progress(PhaseReporting.ToPhaseInfo(currentState, ctx.ContainerName));
                    ctx.FirstValidStatsReceived.TrySetResult();
                }

                ctx.CollectedMetrics.Add(metrics);
                ctx.FirstSampleCollected.TrySetResult();

                logger.LogDebug(
                    "Sample #{Count} for container {ContainerName} - CPU: {Cpu}%, Memory: {Memory}MB",
                    ctx.CollectedMetrics.Count,
                    ctx.ContainerName,
                    metrics.CpuPercent,
                    metrics.MemoryMB);
            }

            logger.LogWarning(
                "Stats stream ended unexpectedly for container {ContainerName}",
                ctx.ContainerName);

            return currentState;
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Streaming cancelled for container {ContainerName}", ctx.ContainerName);
            return currentState;
        }
        catch (Exception ex)
        {
            invalidateCache(ctx.ContainerName.Value);

            var nextState = ConnectionStateMachine.Transition(
                currentState,
                new StreamEvent.Error(ex),
                StreamingConstants.MaxReconnectAttempts,
                StreamingConstants.MaxReconnectDelay);

            if (nextState is ConnectionState.Disconnected d)
            {
                logger.LogWarning(
                    ex,
                    "Stream disconnected for container {ContainerName}, retrying in {Delay:F1}s (attempt {Count}/{Max})",
                    ctx.ContainerName,
                    d.NextBackoff.TotalSeconds,
                    d.ConsecutiveFailures,
                    StreamingConstants.MaxReconnectAttempts);
            }

            return nextState;
        }
    }

    /// <summary>
    /// Waits for the backoff delay, re-resolves the container ID (handles restarts),
    /// and transitions to a new Connecting state.
    /// </summary>
    private static async Task<ConnectionState> ExecuteReconnectAsync(
        MonitorContext ctx,
        ConnectionState.Disconnected disconnectedState,
        GetContainerIdDelegate getContainerId,
        InvalidateContainerCacheDelegate invalidateCache,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var backoff = ReconnectionPolicy.CalculateBackoff(disconnectedState.NextBackoff, StreamingConstants.MaxReconnectDelay);

            await Task.Delay(backoff.DelayToUse, ct);

            invalidateCache(ctx.ContainerName.Value);

            var idResult = await getContainerId(ctx.ContainerName.Value, ct);

            if (idResult.IsFailure)
            {
                logger.LogWarning(
                    "Container {ContainerName} not found during reconnection",
                    ctx.ContainerName);
            }

            var resolvedContainerId = idResult.IsSuccess
                ? ContainerId.FromString(idResult.SuccessValue)
                : disconnectedState.ContainerId;

            return new ConnectionState.Connecting(
                resolvedContainerId,
                disconnectedState.ConsecutiveFailures,
                backoff.NextBackoff);
        }
        catch (OperationCanceledException)
        {
            return disconnectedState;
        }
    }
}
