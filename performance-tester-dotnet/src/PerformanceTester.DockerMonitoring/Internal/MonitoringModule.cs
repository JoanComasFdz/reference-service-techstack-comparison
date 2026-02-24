using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring.Internal.Connection;
using PerformanceTester.DockerMonitoring.ValueObjects;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.Internal;

/// <summary>
/// FP-style module for container monitoring logic (Guideline 02-05).
/// Owns MonitorContext (mutable state), Dependencies record, static operations,
/// and PhaseReporting (internal utility).
/// All functions take MonitorContext and Dependencies as explicit parameters — no instance state.
/// Testable without BackgroundService lifecycle.
/// </summary>
internal static class MonitoringModule
{
    // ── Context Record (Guideline 05-04) ─────────────────────────────────────

    /// <summary>
    /// All mutable state for a single container monitor session.
    /// Owned by DockerMonitorBackgroundService, passed explicitly to static functions.
    /// </summary>
    internal sealed record MonitorContext(NonEmptyString ContainerName)
    {
        public ConcurrentBag<DockerMetrics> CollectedMetrics { get; } = new();
        public TaskCompletionSource StartSignal { get; } = new();
        public TaskCompletionSource FirstSampleCollected { get; } = new();
        public TaskCompletionSource FirstValidStatsReceived { get; } = new();
        public bool StreamingFailed { get; set; }
    }

    // ── Dependencies Record ──────────────────────────────────────────────────

    /// <summary>
    /// Bundles all delegates needed by the monitoring state machine.
    /// Built by <see cref="DockerMonitorBackgroundService"/> from <see cref="StatsModule.Dependencies"/>
    /// plus the runtime progress delegate.
    /// </summary>
    internal record Dependencies(
        StatsModule.GetContainerIdDelegate GetContainerId,
        StatsModule.StreamMetricsDelegate StreamMetrics,
        StatsModule.InvalidateContainerCacheDelegate InvalidateCache,
        ReportDockerMonitorProgressDelegate ReportProgress);

    // ── Execution Methods ────────────────────────────────────────────────────

    /// <summary>
    /// State machine interpreter loop.
    /// Pure <see cref="ConnectionStateMachine.Transition"/> decides state changes;
    /// this method interprets states as effects.
    /// </summary>
    public static async Task RunStreamingLoopAsync(
        MonitorContext ctx,
        ContainerId initialContainerId,
        Dependencies deps,
        ILogger logger,
        CancellationToken ct)
    {
        ConnectionState state = new ConnectionState.Connecting(
            initialContainerId,
            AttemptCount.FromInt(0),
            StreamingConstants.InitialReconnectDelay);

        while (state is not ConnectionState.Failed && !ct.IsCancellationRequested)
        {
            deps.ReportProgress(PhaseReporting.ToPhaseInfo(state, ctx.ContainerName));

            state = state switch
            {
                ConnectionState.Connecting c => await ExecuteStreamingSessionAsync(ctx, c, deps, logger, ct),
                ConnectionState.Disconnected d => await ExecuteReconnectAsync(ctx, d, deps, logger, ct),
                _ => state
            };
        }

        if (state is ConnectionState.Failed)
        {
            deps.ReportProgress(PhaseReporting.ToPhaseInfo(state, ctx.ContainerName));

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
        Dependencies deps,
        ILogger logger,
        CancellationToken ct)
    {
        ConnectionState currentState = connectingState;

        try
        {
            await foreach (var metrics in deps.StreamMetrics(connectingState.ContainerId.Value, ctx.ContainerName, ct))
            {
                if (currentState is ConnectionState.Connecting)
                {
                    currentState = ConnectionStateMachine.Transition(
                        currentState,
                        new StreamEvent.StatsReceived(),
                        StreamingConstants.MaxReconnectAttempts,
                        StreamingConstants.MaxReconnectDelay);

                    deps.ReportProgress(PhaseReporting.ToPhaseInfo(currentState, ctx.ContainerName));
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
            deps.InvalidateCache(ctx.ContainerName.Value);

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
        Dependencies deps,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var backoff = ReconnectionPolicy.CalculateBackoff(disconnectedState.NextBackoff, StreamingConstants.MaxReconnectDelay);

            await Task.Delay(backoff.DelayToUse, ct);

            deps.InvalidateCache(ctx.ContainerName.Value);

            var idResult = await deps.GetContainerId(ctx.ContainerName.Value, ct);

            var resolvedContainerId = idResult.Match(
                success: s => ContainerId.FromString(s.Value),
                failure: f =>
                {
                    logger.LogWarning(
                        "Container {ContainerName} not found during reconnection",
                        ctx.ContainerName);
                    return disconnectedState.ContainerId;
                });

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

    // ── Phase Reporting (internal utility) ────────────────────────────────────

    /// <summary>
    /// Pure mapping from <see cref="ConnectionState"/> to <see cref="DockerMonitorPhaseInfo"/>.
    /// Centralizes phase reporting that was previously scattered across 6+ locations
    /// in DockerMonitorBackgroundService.
    /// </summary>
    internal static class PhaseReporting
    {
        public static DockerMonitorPhaseInfo ToPhaseInfo(
            ConnectionState state,
            NonEmptyString containerName) => state switch
        {
            ConnectionState.Connecting { AttemptNumber.Value: 0 } => DockerMonitorPhaseInfo.Starting(
                DockerMonitorPhase.StreamConnecting,
                containerName,
                message: $"Connecting to {containerName}..."),

            ConnectionState.Connecting c => DockerMonitorPhaseInfo.Starting(
                DockerMonitorPhase.StreamConnecting,
                containerName,
                message: $"Reconnecting to {containerName} (attempt {c.AttemptNumber + 1})..."),

            ConnectionState.Connected => DockerMonitorPhaseInfo.Completed(
                DockerMonitorPhase.StreamConnected,
                containerName,
                message: $"Connected to {containerName}"),

            ConnectionState.Disconnected d => DockerMonitorPhaseInfo.Failed(
                DockerMonitorPhase.StreamDisconnected,
                containerName,
                message: $"Disconnected, retrying in {d.NextBackoff.TotalSeconds:F1}s " +
                         $"(attempt {d.ConsecutiveFailures}/{StreamingConstants.MaxReconnectAttempts})"),

            ConnectionState.Failed f => DockerMonitorPhaseInfo.Failed(
                DockerMonitorPhase.StreamFailed,
                containerName,
                message: $"Connection failed permanently after {f.TotalAttempts} attempts"),

            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
    }
}
