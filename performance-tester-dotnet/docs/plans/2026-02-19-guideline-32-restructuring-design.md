# Design: Restructure Guideline 32 into Three Guidelines

**Date:** 2026-02-19
**Status:** Approved

## Problem

Guideline 32 (Thin Shell Pattern) bundles two independent ideas:

1. **Extracting mutable state** into an explicit context record — a general technique applicable to any class with mutable state.
2. **Thin shell wiring** for framework-coupled classes — a specific application when the class can't be static.

Additionally, the guideline doesn't distinguish between concurrent and sequential state handling, which have fundamentally different approaches (mutable shared state vs. immutable state threading).

## Decision

Replace Guideline 32 with three separate guidelines:

| # | Name | Scope |
|---|---|---|
| **32** | Context Record Pattern (Shared Mutable State) | Mutable state bag for concurrent/async scenarios |
| **33** | Immutable State Threading (Sequential Pipelines) | Return new records via `with` / `Aggregate` for single-threaded code |
| **34** | Thin Shell Pattern for Framework-Coupled Classes | Framework-inheriting class delegates to static functions using 32 or 33 |

## Guideline 32: Context Record Pattern (Shared Mutable State)

**Core idea:** When a class or function group has mutable state, centralize it in a single record. Pass the record explicitly to static functions. The value is **visibility** — all state that can change lives in one place.

**Apply at the first sign of mutable state** — consistency matters more than saving a record definition.

```csharp
// ✅ Good — all mutable state visible in one record
internal sealed record MonitorContext(NonEmptyString ContainerName)
{
    public ConcurrentBag<DockerMetrics> CollectedMetrics { get; } = new();
    public TaskCompletionSource StartSignal { get; } = new();
    public bool StreamingFailed { get; set; }
}

internal static class MonitoringOperations
{
    public static async Task RunStreamingLoopAsync(
        MonitorContext ctx,
        ILogger logger,
        CancellationToken ct)
    {
        ctx.CollectedMetrics.Add(metric);  // Thread-safe mutation
    }
}

// ❌ Avoid — mutable state scattered across private fields
internal sealed class DockerMonitorService
{
    private readonly ConcurrentBag<DockerMetrics> _collectedMetrics = new();
    private readonly TaskCompletionSource _startSignal = new();
    private volatile bool _streamingFailed;
    // Must read entire class to find all mutable state
}
```

**Why mutable, not immutable?** When state is shared across concurrent tasks, returning a new record doesn't work — other threads still hold the old reference. Types like `ConcurrentBag<T>`, `TaskCompletionSource`, and `SemaphoreSlim` are inherently mutable; copying them into a new record would fork the state. For single-threaded pipelines where immutability IS possible, see Guideline 33.

**When to use:** Any class or function group that has mutable state — even a single field.

**When NOT to use:** Purely stateless functions (like `DatabaseCleaner`, `RabbitMqCleaner`).

**Structure:**
- `sealed record` with constructor parameters for fixed identity
- Mutable properties: `{ get; set; }` or self-initializing collections (`= new()`)
- No logic in the record — state bag only

## Guideline 33: Immutable State Threading (Sequential Pipelines)

**Core idea:** When state flows through a single-threaded pipeline, return a new record from each step. The caller reassigns the variable; the record itself is never mutated. This is the FP fold/reduce pattern in C#.

```csharp
// ✅ Good — each step returns new state, no mutation
internal sealed record ParseState(
    int LinesProcessed,
    int ErrorCount,
    bool HeaderFound);

internal static class LogParser
{
    public static ParseState ProcessLine(ParseState state, string line) =>
        IsError(line)
            ? state with { LinesProcessed = state.LinesProcessed + 1, ErrorCount = state.ErrorCount + 1 }
            : state with { LinesProcessed = state.LinesProcessed + 1 };
}

// Usage — pure fold via LINQ Aggregate
var state = lines.Aggregate(
    new ParseState(0, 0, false),
    LogParser.ProcessLine);

// ❌ Avoid — mutable fields in a single-threaded pipeline
var linesProcessed = 0;
var errorCount = 0;
foreach (var line in lines)
{
    linesProcessed++;
    if (IsError(line)) errorCount++;
}
```

**Why this works here but not in Guideline 32:** There is only one reference to the state. Reassigning `state = ...` updates the only copy. No other thread holds a stale reference.

**When to use:** Sequential processing (loops, pipelines, fold/reduce patterns) where state accumulates across steps but is only accessed by one thread.

**When NOT to use:**
- State is shared across concurrent tasks — use Guideline 32.
- State contains inherently mutable types (`ConcurrentBag`, `TaskCompletionSource`) — these can't be copied meaningfully.

**Structure:**
- `sealed record` with all properties in the constructor (positional record)
- All properties are immutable (no `{ get; set; }`)
- Functions return the record type (the new state), not `void`
- Caller uses `Aggregate` or `state = Function(state, input)` pattern

## Guideline 34: Thin Shell Pattern for Framework-Coupled Classes

**Core idea:** When a class can't be static because it inherits from a framework base class, make it a thin shell with three responsibilities only:

1. **Own the context** — a context record (Guideline 32 or 33)
2. **Wire lifecycle** — connect framework hooks to static functions
3. **Expose the public API** — delegate to context or static functions

```csharp
// 1. Context record (Guideline 32)
internal sealed record MonitorContext(NonEmptyString ContainerName)
{
    public ConcurrentBag<DockerMetrics> CollectedMetrics { get; } = new();
    public TaskCompletionSource StartSignal { get; } = new();
    public bool StreamingFailed { get; set; }
}

// 2. Static operations — all logic
internal static class MonitoringOperations
{
    public static async Task RunStreamingLoopAsync(
        MonitorContext ctx, ...) { ... }
}

// 3. Thin shell — owns context, wires lifecycle, no business logic
internal sealed class DockerMonitorService : BackgroundService
{
    private readonly MonitorContext _ctx;

    public DockerMonitorService(NonEmptyString containerName, ...)
    {
        _ctx = new MonitorContext(containerName);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Lifecycle wiring only
        await MonitoringOperations.RunStreamingLoopAsync(_ctx, ...);
    }
}
```

Same pattern applies to API wrappers — use static operations + DI-registered delegates instead of wrapper classes holding clients as fields.

**When to use:**
- A class inherits from a framework base class (`BackgroundService`, `ControllerBase`, `DbContext`)
- A class wraps an external client/SDK as instance state

**When NOT to use:**
- The class has no framework coupling — just make it static (Guideline 1)

**File organization:** `Context.cs` (state record) + `Operations.cs` (static logic) + `Service.cs` (thin shell).

## Changes to Existing Content

- **Delete** current Guideline 32 entirely
- **Add** Guidelines 32, 33, 34 as described above
- **Update** summary table at bottom of `CODING_GUIDELINES.md` to reflect three new rows replacing one
- **No changes** to Guidelines 1–31
