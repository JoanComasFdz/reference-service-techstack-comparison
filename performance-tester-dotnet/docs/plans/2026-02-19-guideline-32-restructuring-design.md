# Restructure Guideline 32 into Three Guidelines — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the monolithic Guideline 32 (Thin Shell Pattern) with three focused guidelines covering context records, immutable state threading, and thin shells.

**Architecture:** Single-file edit to `CODING_GUIDELINES.md`. Replace lines 1121–1261 (current Guideline 32) with three new guidelines, and replace one summary table row with three.

**Tech Stack:** Markdown only.

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

---

## Implementation Tasks

### Task 1: Replace current Guideline 32 with new Guidelines 32, 33, 34

**Files:**
- Modify: `CODING_GUIDELINES.md:1121-1261`

**Step 1: Replace the guideline section**

Replace everything from line 1121 (`### 32. Thin Shell Pattern for Framework-Coupled Classes`) through line 1261 (end of "Relationship to other guidelines" list) with the three new guidelines as defined in the design sections above. Preserve the `---` separator on line 1262.

**Step 2: Verify markdown renders correctly**

Skim the replaced section for broken formatting (unclosed code fences, mismatched headings, table alignment).

**Step 3: Commit**

```bash
git add CODING_GUIDELINES.md
git commit -m "docs: replace Guideline 32 with Guidelines 32 (context record), 33 (immutable state), 34 (thin shell)"
```

---

### Task 2: Update the summary table

**Files:**
- Modify: `CODING_GUIDELINES.md` — summary table at end of file

**Step 1: Replace the summary table row**

Replace the single row:
```
| Thin shell pattern          | Does this class inherit from a framework base? Extract state → context record, logic → static functions       |
```

With three rows:
```
| Context record pattern      | Does this class have mutable state? Extract it into a context record, pass explicitly to static functions     |
| Immutable state threading   | Is this a single-threaded pipeline? Return new records via `with` / `Aggregate`, no mutation                  |
| Thin shell pattern          | Does this class inherit from a framework base? Own context, wire lifecycle, delegate to static functions      |
```

**Step 2: Commit**

```bash
git add CODING_GUIDELINES.md
git commit -m "docs: update summary table for Guidelines 32-34"
```
