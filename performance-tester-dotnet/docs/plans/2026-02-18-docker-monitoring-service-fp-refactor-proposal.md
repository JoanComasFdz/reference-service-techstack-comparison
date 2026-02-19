# Refactoring Plan: DockerMonitorService → Functional Style

## Goal

Refactor `DockerMonitorService` to separate pure decisions from effectful actions, making the reconnection state machine explicit and testable without Docker, async, or mocks.

## Principles

- **Return new state instead of mutating fields** — every "decision" function is pure
- **Explicit state machines via discriminated unions** — no implicit state in volatile fields
- **`Option<T>` / `Result<T,E>` over `T?`** inside domain logic — `T?` only at boundaries (interop, deserialization)
- **`IAsyncEnumerable` pipelines over callbacks** — replaces `OnStatsReceived` callback pattern
- **Effect interpretation at the boundary** — the loop drives effects, pure functions decide transitions

---

## Step 1: Extract Pure Reconnection Policy

**What:** Extract `CalculateReconnectionDelay` and `ShouldRetry` into a static pure module.

**Why:** Currently these mutate `_currentBackoffDelay`, `_consecutiveFailures`, and call `_dockerClient.InvalidateContainerCache` — mixing decisions with effects.

**Create:** `ReconnectionPolicy.cs`

```csharp
public static class ReconnectionPolicy
{
    public readonly record struct BackoffResult(
        TimeSpan DelayToUse,
        TimeSpan NextBackoff);

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

    public static bool ShouldRetry(int consecutiveFailures, int maxAttempts)
        => consecutiveFailures <= maxAttempts;
}
```

**Tests to write:**

- `CalculateBackoff` returns current delay as `DelayToUse` and doubled delay as `NextBackoff`
- `CalculateBackoff` caps at `maxBackoff`
- `ShouldRetry` returns true when under limit, false when at/over limit

---

## Step 2: Extract Pure Stats → Metrics Transformation

**What:** Extract the metric calculation from `OnStatsReceived` into a pure function returning `Option<DockerMetrics>`.

**Why:** `OnStatsReceived` currently mixes validation, metric calculation, 3 TCS signals, and collection mutation in one void callback.

**Create:** `StatsProcessing.cs`

```csharp
public static class StatsProcessing
{
    public static Option<DockerMetrics> TryConvertToMetrics(
        ContainerStatsResponse stats,
        string containerName,
        TimeProvider timeProvider)
    {
        if (!DockerClientWrapper.HasValidPreCpuStats(stats))
            return Option<DockerMetrics>.None;

        return Option.Some(new DockerMetrics
        {
            Timestamp = timeProvider.GetUtcNow().UtcDateTime,
            ContainerId = stats.ID,
            ContainerName = containerName,
            CpuPercent = DockerClientWrapper.CalculateCpuPercent(stats),
            MemoryMB = Math.Round(stats.MemoryStats.Usage / 1024.0 / 1024.0, 2)
        });
    }
}
```

**Notes:**

- Use `TimeProvider` (built-in .NET 8 abstraction) instead of `DateTime.UtcNow` for testability
- Use an `Option<T>` type — either from an existing library in the project or create a minimal one (see Step 2b)

**Tests to write:**

- Returns `None` when `HasValidPreCpuStats` is false
- Returns `Some(metrics)` with correct CPU/memory calculations when stats are valid
- Pure: same input always produces same output (minus timestamp, controlled via `TimeProvider`)

### Step 2b: Minimal Option Type (if not already in project)

If there is no `Option<T>` in the project yet, create a minimal one:

```csharp
public readonly record struct Option<T> where T : notnull
{
    private readonly T? _value;
    public bool IsSome { get; }
    public T Value => IsSome ? _value! : throw new InvalidOperationException("Option is None");

    private Option(T value) { _value = value; IsSome = true; }

    public static Option<T> Some(T value) => new(value);
    public static readonly Option<T> None = default;

    public Option<TResult> Map<TResult>(Func<T, TResult> f) where TResult : notnull
        => IsSome ? Option<TResult>.Some(f(_value!)) : Option<TResult>.None;

    public Option<TResult> Bind<TResult>(Func<T, Option<TResult>> f) where TResult : notnull
        => IsSome ? f(_value!) : Option<TResult>.None;

    public T GetValueOrDefault(T fallback) => IsSome ? _value! : fallback;

    public void Match(Action<T> onSome, Action onNone)
    {
        if (IsSome) onSome(_value!);
        else onNone();
    }
}
```

Check the existing codebase first — if there's already a Result/Option type or a library like LanguageExt or OneOf, use that instead.

---

## Step 3: Model Connection State as Discriminated Union

**What:** Replace the implicit state machine (volatile fields `_consecutiveFailures`, `_hasReceivedValidStats`, `_streamingFailed`, `_currentBackoffDelay`) with an explicit state type.

**Why:** The current code has 4+ mutable fields that together represent connection state but can get out of sync. An explicit union makes impossible states unrepresentable.

**Create:** `ConnectionState.cs`

```csharp
public abstract record ConnectionState
{
    public record Connecting(
        string ContainerId,
        int AttemptNumber,
        TimeSpan NextBackoff) : ConnectionState;

    public record Connected(
        string ContainerId) : ConnectionState;

    public record Disconnected(
        string ContainerId,
        int ConsecutiveFailures,
        TimeSpan NextBackoff,
        Exception LastError) : ConnectionState;

    public record Failed(
        int TotalAttempts,
        Exception LastError) : ConnectionState;
}
```

**Create:** `StreamEvent.cs`

```csharp
public abstract record StreamEvent
{
    public record StatsReceived : StreamEvent;
    public record Error(Exception Exception) : StreamEvent;
    public record Cancelled : StreamEvent;
}
```

---

## Step 4: Pure State Transition Function

**What:** Create a pure function that given `(ConnectionState, StreamEvent) → ConnectionState`.

**Why:** This is the core business logic of reconnection — currently buried in `RunStreamingLoopWithReconnectionAsync`, `ShouldRetry`, `HandleConnectionSuccess`, and `AttemptReconnectionAsync` across ~100 lines of mixed effectful code. As a pure function it's trivially testable.

**Add to:** `ConnectionState.cs` or a new `ConnectionStateMachine.cs`

```csharp
public static class ConnectionStateMachine
{
    public static ConnectionState Transition(
        ConnectionState current,
        StreamEvent streamEvent,
        int maxReconnectAttempts,
        TimeSpan maxReconnectDelay) => (current, streamEvent) switch
    {
        (ConnectionState.Connecting, StreamEvent.StatsReceived) =>
            new ConnectionState.Connected(((ConnectionState.Connecting)current).ContainerId),

        (ConnectionState.Connecting c, StreamEvent.Error e)
            when ReconnectionPolicy.ShouldRetry(c.AttemptNumber + 1, maxReconnectAttempts) =>
            new ConnectionState.Disconnected(
                c.ContainerId, c.AttemptNumber + 1,
                ReconnectionPolicy.CalculateBackoff(c.NextBackoff, maxReconnectDelay).NextBackoff,
                e.Exception),

        (ConnectionState.Connecting c, StreamEvent.Error e) =>
            new ConnectionState.Failed(c.AttemptNumber + 1, e.Exception),

        (ConnectionState.Connected c, StreamEvent.Error e) =>
            new ConnectionState.Disconnected(c.ContainerId, 1,
                StreamingConstants.InitialReconnectDelay, e.Exception),

        (ConnectionState.Disconnected d, StreamEvent.StatsReceived) =>
            new ConnectionState.Connected(d.ContainerId),

        (ConnectionState.Disconnected d, StreamEvent.Error e)
            when ReconnectionPolicy.ShouldRetry(d.ConsecutiveFailures + 1, maxReconnectAttempts) =>
            d with {
                ConsecutiveFailures = d.ConsecutiveFailures + 1,
                NextBackoff = ReconnectionPolicy.CalculateBackoff(d.NextBackoff, maxReconnectDelay).NextBackoff,
                LastError = e.Exception },

        (ConnectionState.Disconnected d, StreamEvent.Error e) =>
            new ConnectionState.Failed(d.ConsecutiveFailures + 1, e.Exception),

        (_, StreamEvent.Cancelled) => current,

        _ => current
    };
}
```

**Tests to write (all pure, no async, no mocks):**

- Connecting + StatsReceived → Connected
- Connecting + Error (under limit) → Disconnected with incremented count
- Connecting + Error (at limit) → Failed
- Connected + Error → Disconnected with count=1
- Disconnected + StatsReceived → Connected (reset)
- Disconnected + Error (under limit) → Disconnected with incremented count and increased backoff
- Disconnected + Error (at limit) → Failed
- Any state + Cancelled → same state (no-op)

---

## Step 5: Replace Callback with IAsyncEnumerable Pipeline

**What:** Replace the `OnStatsReceived` callback with an `IAsyncEnumerable<DockerMetrics>` stream in `DockerClientWrapper`.

**Why:** Callbacks invert control flow, force void returns, and require mutable state for signaling (TCS). `IAsyncEnumerable` gives the consumer control with `await foreach`, and "first valid stats" is simply "first yielded item."

**Modify:** `DockerClientWrapper` — add a new method (keep old one until migration is complete):

```csharp
public async IAsyncEnumerable<DockerMetrics> StreamMetrics(
    string containerId,
    string containerName,
    TimeProvider timeProvider,
    [EnumeratorCancellation] CancellationToken ct)
{
    await foreach (var stats in StreamStatsRawAsync(containerId, ct))
    {
        var metrics = StatsProcessing.TryConvertToMetrics(stats, containerName, timeProvider);
        if (metrics.IsSome)
            yield return metrics.Value;
    }
}
```

This likely requires exposing the raw Docker stats stream as `IAsyncEnumerable<ContainerStatsResponse>` first. Check how `StartStatsStreamAsync` currently works and adapt. If it uses the Docker.DotNet callback pattern internally, wrap it with a `Channel<T>` to bridge to `IAsyncEnumerable`.

---

## Step 6: Rewrite the Streaming Loop as State Interpreter

**What:** Rewrite `RunStreamingLoopWithReconnectionAsync` to use the pure `ConnectionStateMachine.Transition` and interpret states as effects.

**Modify:** `DockerMonitorService.RunStreamingLoopWithReconnectionAsync` → simplified loop:

```csharp
private async Task RunStreamingLoopAsync(string containerId, CancellationToken ct)
{
    var state = new ConnectionState.Connecting(
        containerId, 0, StreamingConstants.InitialReconnectDelay);

    while (state is not ConnectionState.Failed && !ct.IsCancellationRequested)
    {
        // Interpret: perform the effect implied by the current state
        var (nextState, metrics) = state switch
        {
            ConnectionState.Connecting s => await ExecuteConnectAsync(s, ct),
            ConnectionState.Connected s => await ExecuteStreamAsync(s, ct),
            ConnectionState.Disconnected s => await ExecuteReconnectAsync(s, ct),
            _ => (state, Enumerable.Empty<DockerMetrics>())
        };

        // Collect metrics yielded during this phase
        foreach (var m in metrics)
            _collectedMetrics.Add(m);

        // Report phase change
        ReportPhase(nextState);
        state = nextState;
    }
}
```

**Remove from the class after this step:**

- `_consecutiveFailures` field
- `_currentBackoffDelay` field
- `_backoffLock` field
- `_hasReceivedValidStats` field
- `_streamingFailed` field
- `_pendingConnectionSuccess` field
- `HandleConnectionSuccess()` method
- `ShouldRetry()` method
- `CalculateReconnectionDelay()` method
- `AttemptReconnectionAsync()` method
- `OnStatsReceived()` method

---

## Step 7: Simplify Phase Reporting

**What:** Create a pure mapping from `ConnectionState` → `DockerMonitorPhaseInfo`.

**Why:** Phase reporting is currently scattered across 6+ locations in the class. Centralizing it as a pure function from state makes it impossible to forget a report or report the wrong phase.

```csharp
public static DockerMonitorPhaseInfo ToPhaseInfo(
    ConnectionState state,
    string containerName) => state switch
{
    ConnectionState.Connecting { AttemptNumber: 0 } =>
        DockerMonitorPhaseInfo.Starting(
            DockerMonitorPhase.StreamConnecting, containerName,
            message: $"Connecting to {containerName}..."),

    ConnectionState.Connecting c =>
        DockerMonitorPhaseInfo.Starting(
            DockerMonitorPhase.StreamConnecting, containerName,
            message: $"Reconnecting to {containerName} (attempt {c.AttemptNumber + 1})..."),

    ConnectionState.Connected _ =>
        DockerMonitorPhaseInfo.Completed(
            DockerMonitorPhase.StreamConnected, containerName,
            message: $"Connected to {containerName}"),

    ConnectionState.Disconnected d =>
        DockerMonitorPhaseInfo.Failed(
            DockerMonitorPhase.StreamDisconnected, containerName,
            message: $"Disconnected, retrying in {d.NextBackoff.TotalSeconds:F1}s " +
                     $"(attempt {d.ConsecutiveFailures}/{StreamingConstants.MaxReconnectAttempts})"),

    ConnectionState.Failed f =>
        DockerMonitorPhaseInfo.Failed(
            DockerMonitorPhase.StreamFailed, containerName,
            message: $"Connection failed permanently after {f.TotalAttempts} attempts"),

    _ => throw new ArgumentOutOfRangeException(nameof(state))
};
```

---

## Execution Order

| Step | Creates/Modifies                           | Risk                                             | Can be done independently |
| ---- | ------------------------------------------ | ------------------------------------------------ | ------------------------- |
| 1    | `ReconnectionPolicy.cs`                    | Low — pure extraction                            | ✅ Yes                    |
| 2    | `StatsProcessing.cs`, optional `Option<T>` | Low — pure extraction                            | ✅ Yes                    |
| 2b   | `Option.cs` (if needed)                    | Low — utility type                               | ✅ Yes                    |
| 3    | `ConnectionState.cs`, `StreamEvent.cs`     | Low — new types, nothing depends on them yet     | ✅ Yes                    |
| 4    | `ConnectionStateMachine.cs`                | Low — pure function, uses types from 1+3         | After 1, 3                |
| 5    | `DockerClientWrapper` modification         | Medium — changes streaming API surface           | ✅ Yes (additive)         |
| 6    | `DockerMonitorService` rewrite             | High — integrates everything, removes old fields | After 1–5                 |
| 7    | Phase reporting pure mapping               | Low — pure extraction                            | After 3                   |

**Recommended approach:** Do steps 1–4 first (all pure, all testable, zero risk to existing code). Write tests. Then do 5–7 which involve modifying existing code.

---

## Fields/Methods Removed from DockerMonitorService After Refactoring

```
- _consecutiveFailures (volatile int)
- _currentBackoffDelay (TimeSpan)
- _backoffLock (Lock)
- _hasReceivedValidStats (volatile bool)
- _streamingFailed (volatile bool)
- _pendingConnectionSuccess (TaskCompletionSource?)
- _firstValidStatsReceived (TaskCompletionSource)
- HandleConnectionSuccess()
- ShouldRetry()
- CalculateReconnectionDelay()
- AttemptReconnectionAsync()
- OnStatsReceived()
```

What remains in the class: DI wiring, `ExecuteAsync` lifecycle, effect interpretation loop, and metric collection. The class becomes thin orchestration glue over pure, tested modules.
