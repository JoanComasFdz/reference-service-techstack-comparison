# DockerMonitorService FP Refactor Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Refactor `DockerMonitorService` to separate pure decisions from effectful actions, making the reconnection state machine explicit.

**Architecture:** Extract pure functions (`ReconnectionPolicy`, `StatsProcessing`, `ConnectionStateMachine`, `PhaseReporting`) into internal static classes. Model connection state as abstract sealed records. Replace callback-based streaming with `IAsyncEnumerable` pipeline. Rewrite the streaming loop as a state interpreter that drives effects based on pure state transitions.

**Tech Stack:** .NET 9, Docker.DotNet, System.Threading.Channels

**Testing philosophy:** Per [TESTING_STRATEGY.md](../01.TESTING_STRATEGY.md), no unit tests are added. The extracted pure functions are simple calculations, mappings, and a straightforward state machine — all validated through the existing integration tests (Task 8). Unit tests for these would violate the project's integration-first philosophy.

**Proposal:** [2026-02-18-docker-monitoring-service-fp-refactor-proposal.md](./2026-02-18-docker-monitoring-service-fp-refactor-proposal.md)

**Adaptations from proposal:**
- Abstract sealed records instead of Dunet `[Union]` for `ConnectionState`/`StreamEvent` (tuple pattern matching in transition function is incompatible with Dunet's `Match()`)
- `DockerMetrics?` instead of `Option<T>` (no Option type exists in codebase; nullable is simpler and sufficient for one function)
- `HasValidPreCpuStats` and `CalculateCpuPercent` move from `DockerClientWrapper` to `StatsProcessing` (they are pure stats processing logic, not Docker client concerns)
- Backoff stored in `Disconnected.NextBackoff` is the delay to **wait** (not the pre-calculated next value), matching the original `CalculateReconnectionDelay` behavior (1s → 2s → 4s)

---

## Task 1: ReconnectionPolicy — Pure Extraction

**Files:**
- Create: `src/PerformanceTester.DockerMonitoring/ReconnectionPolicy.cs`

**Step 1: Write implementation**

Create `src/PerformanceTester.DockerMonitoring/ReconnectionPolicy.cs`:

```csharp
namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Pure functions for reconnection backoff and retry decisions.
/// No side effects, no state — all inputs explicit, all outputs via return value.
/// </summary>
internal static class ReconnectionPolicy
{
    public readonly record struct BackoffResult(
        TimeSpan DelayToUse,
        TimeSpan NextBackoff);

    /// <summary>
    /// Calculates the current delay and the next backoff using exponential doubling with jitter.
    /// Returns <paramref name="currentBackoff"/> as the delay to use now,
    /// and min(currentBackoff * 2, maxBackoff) + jitter as the next backoff.
    /// </summary>
    public static BackoffResult CalculateBackoff(
        TimeSpan currentBackoff,
        TimeSpan maxBackoff,
        int jitterMaxMs = 500)
    {
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, jitterMaxMs));
        var nextBackoff = TimeSpan.FromTicks(
            Math.Min(currentBackoff.Ticks * 2, maxBackoff.Ticks)) + jitter;
        return new(currentBackoff, nextBackoff);
    }

    /// <summary>
    /// Returns true if retrying is allowed (failures have not exceeded the limit).
    /// </summary>
    public static bool ShouldRetry(int consecutiveFailures, int maxAttempts)
        => consecutiveFailures <= maxAttempts;
}
```

**Step 2: Verify build**

Run: `dotnet build src/PerformanceTester.DockerMonitoring`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

**Step 3: Commit**

```bash
git add src/PerformanceTester.DockerMonitoring/ReconnectionPolicy.cs
git commit -m "refactor: extract pure ReconnectionPolicy from DockerMonitorService"
```

---

## Task 2: StatsProcessing — Pure Extraction

**Files:**
- Create: `src/PerformanceTester.DockerMonitoring/StatsProcessing.cs`

**Step 1: Write implementation**

Create `src/PerformanceTester.DockerMonitoring/StatsProcessing.cs`:

```csharp
using Docker.DotNet.Models;

namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Pure functions for converting Docker stats responses into domain metrics.
/// Moved from DockerClientWrapper (HasValidPreCpuStats, CalculateCpuPercent)
/// plus new TryConvertToMetrics composition.
/// </summary>
internal static class StatsProcessing
{
    /// <summary>
    /// Converts a raw Docker stats response into a <see cref="DockerMetrics"/> if the stats are valid.
    /// Returns null when PreCPUStats are invalid (first stats push from Docker has zeroed values).
    /// </summary>
    public static DockerMetrics? TryConvertToMetrics(
        ContainerStatsResponse stats,
        string containerName,
        DateTime timestamp)
    {
        if (!HasValidPreCpuStats(stats))
        {
            return null;
        }

        return new DockerMetrics
        {
            Timestamp = timestamp,
            ContainerId = stats.ID,
            ContainerName = containerName,
            CpuPercent = CalculateCpuPercent(stats),
            MemoryMB = Math.Round(stats.MemoryStats.Usage / 1024.0 / 1024.0, 2)
        };
    }

    /// <summary>
    /// Validates that ContainerStatsResponse has valid PreCPUStats for CPU calculation.
    /// The first stats from a stream often have zeroed PreCPUStats.
    /// </summary>
    public static bool HasValidPreCpuStats(ContainerStatsResponse stats)
        => stats.PreCPUStats.SystemUsage > 0;

    /// <summary>
    /// Calculates CPU percentage from Docker stats.
    /// Formula: (cpu_delta / system_delta) * cpu_count * 100
    /// </summary>
    /// <remarks>
    /// Matches Docker CLI (<c>docker stats</c>) and Python reference implementation.
    /// Stateless: Docker API provides both current and previous stats in a single response.
    /// </remarks>
    public static double CalculateCpuPercent(ContainerStatsResponse stats)
    {
        var cpuDelta = stats.CPUStats.CPUUsage.TotalUsage -
                       stats.PreCPUStats.CPUUsage.TotalUsage;

        var systemDelta = stats.CPUStats.SystemUsage -
                          stats.PreCPUStats.SystemUsage;

        var cpuCount = stats.CPUStats.OnlineCPUs;

        if (systemDelta > 0 && cpuDelta > 0)
        {
            var cpuPercent = (double)cpuDelta / systemDelta * cpuCount * 100.0;
            return Math.Round(cpuPercent, 2);
        }

        return 0.0;
    }
}
```

**Step 2: Verify build**

Run: `dotnet build src/PerformanceTester.DockerMonitoring`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

**Step 3: Commit**

```bash
git add src/PerformanceTester.DockerMonitoring/StatsProcessing.cs
git commit -m "refactor: extract pure StatsProcessing from DockerClientWrapper"
```

---

## Task 3: ConnectionState + StreamEvent — Discriminated Unions

**Files:**
- Create: `src/PerformanceTester.DockerMonitoring/ConnectionState.cs`
- Create: `src/PerformanceTester.DockerMonitoring/StreamEvent.cs`

**Step 1: Create ConnectionState**

Create `src/PerformanceTester.DockerMonitoring/ConnectionState.cs`:

```csharp
namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Explicit state machine for Docker streaming connection lifecycle.
/// Replaces implicit state in volatile fields (_consecutiveFailures, _hasReceivedValidStats,
/// _streamingFailed, _currentBackoffDelay).
/// </summary>
internal abstract record ConnectionState
{
    /// <summary>
    /// Attempting to connect to Docker stats stream.
    /// </summary>
    public sealed record Connecting(
        string ContainerId,
        int AttemptNumber,
        TimeSpan NextBackoff) : ConnectionState;

    /// <summary>
    /// Successfully receiving stats from Docker.
    /// </summary>
    public sealed record Connected(
        string ContainerId) : ConnectionState;

    /// <summary>
    /// Connection lost, will retry after waiting <see cref="NextBackoff"/>.
    /// </summary>
    public sealed record Disconnected(
        string ContainerId,
        int ConsecutiveFailures,
        TimeSpan NextBackoff,
        Exception LastError) : ConnectionState;

    /// <summary>
    /// Connection failed permanently (max retries exceeded).
    /// </summary>
    public sealed record Failed(
        int TotalAttempts,
        Exception LastError) : ConnectionState;
}
```

**Step 2: Create StreamEvent**

Create `src/PerformanceTester.DockerMonitoring/StreamEvent.cs`:

```csharp
namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Events that can occur during a Docker stats streaming session.
/// Used as input to <see cref="ConnectionStateMachine.Transition"/>.
/// </summary>
internal abstract record StreamEvent
{
    /// <summary>Valid stats were received from the stream.</summary>
    public sealed record StatsReceived : StreamEvent;

    /// <summary>An error occurred in the stream.</summary>
    public sealed record Error(Exception Exception) : StreamEvent;

    /// <summary>The stream was cancelled (normal shutdown).</summary>
    public sealed record Cancelled : StreamEvent;
}
```

**Step 3: Verify build**

Run: `dotnet build src/PerformanceTester.DockerMonitoring`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

**Step 4: Commit**

```bash
git add src/PerformanceTester.DockerMonitoring/ConnectionState.cs src/PerformanceTester.DockerMonitoring/StreamEvent.cs
git commit -m "refactor: add ConnectionState and StreamEvent discriminated unions"
```

---

## Task 4: ConnectionStateMachine — Pure Transition Function

**Files:**
- Create: `src/PerformanceTester.DockerMonitoring/ConnectionStateMachine.cs`

**Step 1: Write implementation**

Create `src/PerformanceTester.DockerMonitoring/ConnectionStateMachine.cs`:

```csharp
namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Pure state transition function for Docker streaming connection lifecycle.
/// Given (current state, event) → next state. No side effects.
/// </summary>
internal static class ConnectionStateMachine
{
    /// <summary>
    /// Computes the next connection state given the current state and a stream event.
    /// </summary>
    public static ConnectionState Transition(
        ConnectionState current,
        StreamEvent streamEvent,
        int maxReconnectAttempts,
        TimeSpan maxReconnectDelay) => (current, streamEvent) switch
    {
        // Connecting + valid stats → Connected
        (ConnectionState.Connecting c, StreamEvent.StatsReceived) =>
            new ConnectionState.Connected(c.ContainerId),

        // Connecting + error (under limit) → Disconnected
        (ConnectionState.Connecting c, StreamEvent.Error e)
            when ReconnectionPolicy.ShouldRetry(c.AttemptNumber + 1, maxReconnectAttempts) =>
            new ConnectionState.Disconnected(
                c.ContainerId,
                c.AttemptNumber + 1,
                c.NextBackoff,
                e.Exception),

        // Connecting + error (at limit) → Failed
        (ConnectionState.Connecting c, StreamEvent.Error e) =>
            new ConnectionState.Failed(c.AttemptNumber + 1, e.Exception),

        // Connected + error → Disconnected (reset to count=1, initial backoff)
        (ConnectionState.Connected c, StreamEvent.Error e) =>
            new ConnectionState.Disconnected(
                c.ContainerId,
                1,
                StreamingConstants.InitialReconnectDelay,
                e.Exception),

        // Disconnected + valid stats → Connected (reset)
        (ConnectionState.Disconnected d, StreamEvent.StatsReceived) =>
            new ConnectionState.Connected(d.ContainerId),

        // Disconnected + error (under limit) → Disconnected with incremented count
        (ConnectionState.Disconnected d, StreamEvent.Error e)
            when ReconnectionPolicy.ShouldRetry(d.ConsecutiveFailures + 1, maxReconnectAttempts) =>
            d with
            {
                ConsecutiveFailures = d.ConsecutiveFailures + 1,
                NextBackoff = ReconnectionPolicy.CalculateBackoff(
                    d.NextBackoff, maxReconnectDelay).NextBackoff,
                LastError = e.Exception
            },

        // Disconnected + error (at limit) → Failed
        (ConnectionState.Disconnected d, StreamEvent.Error e) =>
            new ConnectionState.Failed(d.ConsecutiveFailures + 1, e.Exception),

        // Any state + Cancelled → no-op
        (_, StreamEvent.Cancelled) => current,

        // Default: no-op (safety net)
        _ => current
    };
}
```

**Step 2: Verify build**

Run: `dotnet build src/PerformanceTester.DockerMonitoring`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

**Step 3: Commit**

```bash
git add src/PerformanceTester.DockerMonitoring/ConnectionStateMachine.cs
git commit -m "refactor: add pure ConnectionStateMachine transition function"
```

---

## Task 5: PhaseReporting — Pure Mapping

**Files:**
- Create: `src/PerformanceTester.DockerMonitoring/PhaseReporting.cs`

**Step 1: Write implementation**

Create `src/PerformanceTester.DockerMonitoring/PhaseReporting.cs`:

```csharp
namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Pure mapping from <see cref="ConnectionState"/> to <see cref="DockerMonitorPhaseInfo"/>.
/// Centralizes phase reporting that was previously scattered across 6+ locations
/// in DockerMonitorService.
/// </summary>
internal static class PhaseReporting
{
    public static DockerMonitorPhaseInfo ToPhaseInfo(
        ConnectionState state,
        string containerName) => state switch
    {
        ConnectionState.Connecting { AttemptNumber: 0 } =>
            DockerMonitorPhaseInfo.Starting(
                DockerMonitorPhase.StreamConnecting,
                containerName,
                message: $"Connecting to {containerName}..."),

        ConnectionState.Connecting c =>
            DockerMonitorPhaseInfo.Starting(
                DockerMonitorPhase.StreamConnecting,
                containerName,
                message: $"Reconnecting to {containerName} (attempt {c.AttemptNumber + 1})..."),

        ConnectionState.Connected =>
            DockerMonitorPhaseInfo.Completed(
                DockerMonitorPhase.StreamConnected,
                containerName,
                message: $"Connected to {containerName}"),

        ConnectionState.Disconnected d =>
            DockerMonitorPhaseInfo.Failed(
                DockerMonitorPhase.StreamDisconnected,
                containerName,
                message: $"Disconnected, retrying in {d.NextBackoff.TotalSeconds:F1}s " +
                         $"(attempt {d.ConsecutiveFailures}/{StreamingConstants.MaxReconnectAttempts})"),

        ConnectionState.Failed f =>
            DockerMonitorPhaseInfo.Failed(
                DockerMonitorPhase.StreamFailed,
                containerName,
                message: $"Connection failed permanently after {f.TotalAttempts} attempts"),

        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
```

**Step 2: Verify build**

Run: `dotnet build src/PerformanceTester.DockerMonitoring`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

**Step 3: Commit**

```bash
git add src/PerformanceTester.DockerMonitoring/PhaseReporting.cs
git commit -m "refactor: add pure PhaseReporting state-to-phase mapping"
```

---

## Task 6: IAsyncEnumerable StreamMetrics Pipeline

**Files:**
- Modify: `src/PerformanceTester.DockerMonitoring/DockerClientWrapper.cs`

**Step 1: Add StreamStatsRawAsync method (Channel bridge)**

Add to `DockerClientWrapper.cs`, after the existing `StartStatsStreamAsync` method:

```csharp
    /// <summary>
    /// Streams raw Docker stats as an async enumerable.
    /// Bridges Docker.DotNet's IProgress callback to IAsyncEnumerable via Channel.
    /// </summary>
    public async IAsyncEnumerable<ContainerStatsResponse> StreamStatsRawAsync(
        string containerId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<ContainerStatsResponse>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleWriter = true });

        var progress = new Progress<ContainerStatsResponse>(stats =>
            channel.Writer.TryWrite(stats));

        // Start streaming in background — completes when cancelled or errored
        _ = Task.Run(async () =>
        {
            try
            {
                await _client.Containers.GetContainerStatsAsync(
                    containerId,
                    new ContainerStatsParameters { Stream = true },
                    progress,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(
                    ex,
                    "Stats stream error for container {ContainerId}",
                    containerId[..12]);
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        await foreach (var stats in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return stats;
        }
    }
```

**Step 2: Add StreamMetrics method (composed pipeline)**

Add to `DockerClientWrapper.cs`, after `StreamStatsRawAsync`:

```csharp
    /// <summary>
    /// Streams validated Docker metrics as an async enumerable.
    /// Composes raw stats stream with <see cref="StatsProcessing.TryConvertToMetrics"/>,
    /// filtering out invalid stats (zeroed PreCPUStats).
    /// </summary>
    public async IAsyncEnumerable<DockerMetrics> StreamMetrics(
        string containerId,
        string containerName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var stats in StreamStatsRawAsync(containerId, cancellationToken))
        {
            var metrics = StatsProcessing.TryConvertToMetrics(
                stats, containerName, DateTime.UtcNow);

            if (metrics != null)
            {
                yield return metrics;
            }
        }
    }
```

**Step 3: Build and verify**

Run: `dotnet build src/PerformanceTester.DockerMonitoring`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

**Step 4: Commit**

```bash
git add src/PerformanceTester.DockerMonitoring/DockerClientWrapper.cs
git commit -m "refactor: add IAsyncEnumerable StreamMetrics pipeline to DockerClientWrapper"
```

---

## Task 7: Rewrite DockerMonitorService Streaming Loop

This is the high-risk integration step. It replaces the callback-based streaming loop with the state machine interpreter, removes old mutable fields, and integrates all pure modules.

**Files:**
- Modify: `src/PerformanceTester.DockerMonitoring/DockerMonitorService.cs`
- Modify: `src/PerformanceTester.DockerMonitoring/DockerClientWrapper.cs` (remove moved static methods)

**Step 1: Remove HasValidPreCpuStats and CalculateCpuPercent from DockerClientWrapper**

Delete the `HasValidPreCpuStats` and `CalculateCpuPercent` methods from `DockerClientWrapper.cs`. These have been moved to `StatsProcessing.cs`. Keep all other methods (`GetContainerIdAsync`, `StartStatsStreamAsync`, `GetContainerStatsAsync`, `InvalidateContainerCache`, `StreamStatsRawAsync`, `StreamMetrics`, `Dispose`).

The `GetContainerStatsAsync` snapshot method (used for warmup) does NOT call these methods, so the removal is safe.

**Step 2: Rewrite DockerMonitorService**

Replace the entire content of `src/PerformanceTester.DockerMonitoring/DockerMonitorService.cs` with:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// BackgroundService that monitors Docker container resource usage using streaming mode.
/// Uses explicit ConnectionState machine for reconnection logic and IAsyncEnumerable for streaming.
/// Named delegates in DI provide public access to collected metrics.
/// Supports deferred start pattern — waits for StartMonitoring() before collecting metrics.
/// </summary>
internal sealed class DockerMonitorService : BackgroundService
{
    private readonly string _containerName;
    private readonly DockerClientWrapper _dockerClient;
    private readonly ILogger<DockerMonitorService> _logger;
    private readonly ConcurrentBag<DockerMetrics> _collectedMetrics = new();
    private readonly TaskCompletionSource _startSignal = new();
    private readonly TaskCompletionSource _firstSampleCollected = new();
    private readonly TaskCompletionSource _firstValidStatsReceived = new();

    private Task? _streamingTask;
    private CancellationTokenSource? _streamingCts;
    private volatile bool _streamingFailed;

    private ReportDockerMonitorProgress? _progress;
    private bool _started;

    public string ContainerName => _containerName;

    public DockerMonitorService(
        string containerName,
        DockerClientWrapper dockerClient,
        ILogger<DockerMonitorService> logger)
    {
        _containerName = containerName;
        _dockerClient = dockerClient;
        _logger = logger;
    }

    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Warming up Docker API for container {ContainerName}...", _containerName);

        var containerId = await _dockerClient.GetContainerIdAsync(_containerName, cancellationToken);

        if (containerId != null)
        {
            await _dockerClient.GetContainerStatsAsync(_containerName, cancellationToken);
        }

        _logger.LogDebug("Docker API warmup complete for container {ContainerName}", _containerName);
    }

    public async Task StartMonitoringAsync(
        ReportDockerMonitorProgress? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            throw new InvalidOperationException($"Monitoring has already been started for container {_containerName}");
        }

        _started = true;
        _progress = progress;

        _progress?.Invoke(DockerMonitorPhaseInfo.Starting(
            DockerMonitorPhase.MonitoringRequested,
            _containerName,
            message: $"Starting streaming monitor for container {_containerName}"));

        _startSignal.TrySetResult();
        _logger.LogInformation(
            "StartMonitoring called for container {ContainerName}, waiting for first sample...",
            _containerName);

        await _firstSampleCollected.Task.WaitAsync(cancellationToken);

        _logger.LogInformation("First sample collected for container {ContainerName}", _containerName);
    }

    public IReadOnlyCollection<DockerMetrics> GetCollectedMetrics() => _collectedMetrics
            .OrderBy(m => m.Timestamp)
            .ToList()
            .AsReadOnly();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Docker monitor BackgroundService started for container: {ContainerName}, waiting for StartMonitoring() call...",
            _containerName);

        // Wait for StartMonitoring() to be called (deferred start pattern)
        try
        {
            await _startSignal.Task.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Docker monitor stopped before StartMonitoring() was called for container: {ContainerName}",
                _containerName);
            return;
        }

        // Resolve container ID once
        string? containerId;
        try
        {
            containerId = await _dockerClient.GetContainerIdAsync(_containerName, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve container {ContainerName}", _containerName);
            _firstSampleCollected.TrySetResult();
            _progress?.Invoke(DockerMonitorPhaseInfo.Failed(
                DockerMonitorPhase.StreamFailed,
                _containerName,
                message: $"Failed to resolve container: {ex.Message}"));
            return;
        }

        if (containerId == null)
        {
            _logger.LogWarning("Container {ContainerName} not found, cannot start monitoring", _containerName);
            _firstSampleCollected.TrySetResult();
            _progress?.Invoke(DockerMonitorPhaseInfo.Failed(
                DockerMonitorPhase.StreamFailed,
                _containerName,
                message: $"Container '{_containerName}' not found"));
            return;
        }

        _logger.LogInformation(
            "Docker monitor starting streaming for container: {ContainerName} (ID: {ContainerId})",
            _containerName,
            containerId[..12]);

        // Start streaming in background task with state machine reconnection
        _streamingCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _streamingTask = Task.Run(
            () => RunStreamingLoopAsync(containerId, _streamingCts.Token),
            stoppingToken);

        // Wait for first valid stats from stream
        try
        {
            await _firstValidStatsReceived.Task.WaitAsync(StreamingConstants.FirstStatsTimeout, stoppingToken);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Timeout waiting for first valid stats from container {ContainerName} after {Timeout}s",
                _containerName,
                StreamingConstants.FirstStatsTimeout.TotalSeconds);
        }

        // Wait for cancellation (streaming loop runs independently)
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Docker monitor stopping for container: {ContainerName}",
                _containerName);
        }
        finally
        {
            _streamingCts?.Cancel();

            try
            {
                if (_streamingTask != null)
                {
                    await _streamingTask.WaitAsync(StreamingConstants.StreamingShutdownTimeout);
                }
            }
            catch { /* Ignore timeout or exceptions */ }

            _streamingCts?.Dispose();

            _logger.LogInformation(
                "Docker monitor completed for container: {ContainerName}, collected {Count} samples",
                _containerName,
                _collectedMetrics.Count);

            if (!_streamingFailed)
            {
                _progress?.Invoke(DockerMonitorPhaseInfo.Completed(
                    DockerMonitorPhase.MonitoringCompleted,
                    _containerName,
                    sampleCount: _collectedMetrics.Count,
                    message: $"Monitoring completed, collected {_collectedMetrics.Count} samples"));
            }
        }
    }

    /// <summary>
    /// State machine interpreter loop. Pure <see cref="ConnectionStateMachine.Transition"/>
    /// decides state changes; this method interprets states as effects.
    /// </summary>
    private async Task RunStreamingLoopAsync(
        string initialContainerId,
        CancellationToken cancellationToken)
    {
        ConnectionState state = new ConnectionState.Connecting(
            initialContainerId, 0, StreamingConstants.InitialReconnectDelay);

        while (state is not ConnectionState.Failed && !cancellationToken.IsCancellationRequested)
        {
            _progress?.Invoke(PhaseReporting.ToPhaseInfo(state, _containerName));

            state = state switch
            {
                ConnectionState.Connecting c => await ExecuteStreamingSessionAsync(c, cancellationToken),
                ConnectionState.Disconnected d => await ExecuteReconnectAsync(d, cancellationToken),
                _ => state
            };
        }

        if (state is ConnectionState.Failed)
        {
            _progress?.Invoke(PhaseReporting.ToPhaseInfo(state, _containerName));
            _streamingFailed = true;

            _logger.LogError(
                "Streaming failed permanently for container {ContainerName}",
                _containerName);
        }
    }

    /// <summary>
    /// Starts a streaming session: iterates the IAsyncEnumerable metrics stream,
    /// signals connection success on first valid item, collects all metrics.
    /// Returns the next state based on how the stream ended.
    /// </summary>
    private async Task<ConnectionState> ExecuteStreamingSessionAsync(
        ConnectionState.Connecting connectingState,
        CancellationToken cancellationToken)
    {
        ConnectionState currentState = connectingState;

        try
        {
            await foreach (var metrics in _dockerClient.StreamMetrics(
                connectingState.ContainerId, _containerName, cancellationToken))
            {
                if (currentState is ConnectionState.Connecting)
                {
                    // First valid stats → transition to Connected
                    currentState = ConnectionStateMachine.Transition(
                        currentState,
                        new StreamEvent.StatsReceived(),
                        StreamingConstants.MaxReconnectAttempts,
                        StreamingConstants.MaxReconnectDelay);

                    _progress?.Invoke(PhaseReporting.ToPhaseInfo(currentState, _containerName));
                    _firstValidStatsReceived.TrySetResult();
                }

                _collectedMetrics.Add(metrics);
                _firstSampleCollected.TrySetResult();

                _logger.LogDebug(
                    "Sample #{Count} for container {ContainerName} - CPU: {Cpu}%, Memory: {Memory}MB",
                    _collectedMetrics.Count,
                    _containerName,
                    metrics.CpuPercent,
                    metrics.MemoryMB);
            }

            // Stream ended without error (unexpected for Stream=true)
            _logger.LogWarning(
                "Stats stream ended unexpectedly for container {ContainerName}",
                _containerName);

            return currentState;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Streaming cancelled for container {ContainerName}", _containerName);
            return currentState;
        }
        catch (Exception ex)
        {
            _dockerClient.InvalidateContainerCache(_containerName);

            var nextState = ConnectionStateMachine.Transition(
                currentState,
                new StreamEvent.Error(ex),
                StreamingConstants.MaxReconnectAttempts,
                StreamingConstants.MaxReconnectDelay);

            if (nextState is ConnectionState.Disconnected d)
            {
                _logger.LogWarning(
                    ex,
                    "Stream disconnected for container {ContainerName}, retrying in {Delay:F1}s (attempt {Count}/{Max})",
                    _containerName,
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
    private async Task<ConnectionState> ExecuteReconnectAsync(
        ConnectionState.Disconnected disconnectedState,
        CancellationToken cancellationToken)
    {
        try
        {
            var backoff = ReconnectionPolicy.CalculateBackoff(
                disconnectedState.NextBackoff, StreamingConstants.MaxReconnectDelay);

            await Task.Delay(backoff.DelayToUse, cancellationToken);

            _dockerClient.InvalidateContainerCache(_containerName);

            var newContainerId = await _dockerClient.GetContainerIdAsync(
                _containerName, cancellationToken);

            if (newContainerId == null)
            {
                _logger.LogWarning(
                    "Container {ContainerName} not found during reconnection",
                    _containerName);
            }

            return new ConnectionState.Connecting(
                newContainerId ?? disconnectedState.ContainerId,
                disconnectedState.ConsecutiveFailures,
                backoff.NextBackoff);
        }
        catch (OperationCanceledException)
        {
            return disconnectedState;
        }
    }
}
```

**Step 3: Build entire solution**

Run: `dotnet build /workspace/performance-tester-dotnet`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

If there are compilation errors from other projects referencing `DockerClientWrapper.HasValidPreCpuStats` or `DockerClientWrapper.CalculateCpuPercent`, update those call sites to use `StatsProcessing.HasValidPreCpuStats` and `StatsProcessing.CalculateCpuPercent` instead.

**Step 4: Commit**

```bash
git add src/PerformanceTester.DockerMonitoring/DockerMonitorService.cs src/PerformanceTester.DockerMonitoring/DockerClientWrapper.cs
git commit -m "refactor: rewrite DockerMonitorService streaming loop as state machine interpreter"
```

---

## Task 8: Integration Test Verification

**Files:**
- No new files — verification only.

**Step 1: Run DockerMonitoring integration tests**

Run: `dotnet test src/PerformanceTester.DockerMonitoring.IntegrationTests --logger "console;verbosity=detailed"`
Expected: All 9 tests pass. This validates that the refactoring preserves behavior:
- Container streaming still works
- Phase reporting sequence is preserved
- Metrics collection works
- Graceful shutdown works
- Container-not-found still reports StreamFailed

**Step 2: Fix any failures**

If integration tests fail, the most likely causes are:
1. Phase reporting order changed — check `PhaseReporting.ToPhaseInfo` matches expected phases
2. `_firstSampleCollected` or `_firstValidStatsReceived` not signaled — check `ExecuteStreamingSessionAsync`
3. `StreamMetrics` pipeline drops items — check `StatsProcessing.TryConvertToMetrics` null handling

**Step 3: Run full solution build and test**

Run: `dotnet build /workspace/performance-tester-dotnet && dotnet test /workspace/performance-tester-dotnet`
Expected: Full build succeeds, all tests across all projects pass.

**Step 4: Commit (if any fixes were made)**

```bash
git add -u
git commit -m "fix: resolve integration test issues from DockerMonitorService refactoring"
```

---

## Removed from DockerMonitorService

After the refactoring, these fields and methods are removed:

```
REMOVED:
- _consecutiveFailures (volatile int)
- _currentBackoffDelay (TimeSpan)
- _backoffLock (Lock)
- _hasReceivedValidStats (volatile bool)
- _pendingConnectionSuccess (TaskCompletionSource?)
- HandleConnectionSuccess() method
- ShouldRetry() method
- CalculateReconnectionDelay() method
- AttemptReconnectionAsync() method
- OnStatsReceived() method
- RunStreamingLoopWithReconnectionAsync() method

KEPT:
- _containerName, _dockerClient, _logger (DI dependencies)
- _collectedMetrics (ConcurrentBag — metrics storage)
- _startSignal, _firstSampleCollected, _firstValidStatsReceived (lifecycle TCS)
- _streamingTask, _streamingCts (streaming lifecycle)
- _streamingFailed (completion reporting flag)
- _progress, _started (deferred start pattern)
- WarmupAsync() — unchanged
- StartMonitoringAsync() — unchanged
- GetCollectedMetrics() — unchanged
- ExecuteAsync() — unchanged structure, uses new streaming loop

ADDED:
- RunStreamingLoopAsync() — state machine interpreter loop
- ExecuteStreamingSessionAsync() — effect handler for Connecting state
- ExecuteReconnectAsync() — effect handler for Disconnected state
```

## Moved from DockerClientWrapper to StatsProcessing

```
MOVED:
- HasValidPreCpuStats(ContainerStatsResponse) → StatsProcessing.HasValidPreCpuStats
- CalculateCpuPercent(ContainerStatsResponse) → StatsProcessing.CalculateCpuPercent

ADDED to DockerClientWrapper:
- StreamStatsRawAsync() — IAsyncEnumerable bridge via Channel
- StreamMetrics() — composed pipeline (raw stats → validated metrics)
```

## New Files Created

| File | Type | Purpose |
|------|------|---------|
| `ReconnectionPolicy.cs` | Internal static class | Pure backoff calculation and retry decisions |
| `StatsProcessing.cs` | Internal static class | Pure stats → metrics transformation |
| `ConnectionState.cs` | Internal abstract record | Explicit connection state machine |
| `StreamEvent.cs` | Internal abstract record | Stream lifecycle events |
| `ConnectionStateMachine.cs` | Internal static class | Pure state transition function |
| `PhaseReporting.cs` | Internal static class | Pure state → phase info mapping |

**Testing:** No unit tests added. All pure extractions are validated through the existing DockerMonitoring integration tests (Task 8), per the project's [integration-first testing strategy](../01.TESTING_STRATEGY.md).
