# State and Composition Patterns

> Guidelines 23-24, 31-34. For the full index and routing table, see [CODING_GUIDELINES.md](../CODING_GUIDELINES.md).

Advanced patterns for composing functions, binding lambdas, and managing state. Covers higher-order helpers, lambda binding lifetime buckets, mutable vs immutable state records, and the thin shell pattern for framework-coupled classes.

---

## Function Composition

### 23. Higher-Order Helper Functions for Structural Duplication

When the same structure (e.g., iterate + await all) is repeated across call sites with only the operation changing, extract the structure as a function that takes a function.

```csharp
// ❌ Structural duplication — same pattern, different operation
async () => { await Task.WhenAll(monitors.Select(m => m.WarmupAsync(ct))); }
async () => { await Task.WhenAll(monitors.Select(m => m.StartAsync(ct))); }

// ✅ Higher-order helper captures the repeated structure
Task forAllMonitors(Func<IMonitor, Task> action) => Task.WhenAll(monitors.Select(action));
() => forAllMonitors(m => m.WarmupAsync(ct))
() => forAllMonitors(m => m.StartAsync(ct))
```

**When to use:** Two or more call sites share identical structure but plug in different operations.

**When NOT to use:** Only one call site, or the structure is trivial (one-liner with no repetition).

### 24. Behavioral Decisions Belong in the Consumer, Not the Caller

When a class receives a shared function whose signature has a parameter it doesn't need, that class should accept the full signature and provide the default internally. The caller shouldn't encode knowledge about what the consumer does or doesn't care about.

```csharp
// ❌ Caller decides what the consumer needs — leaks knowledge outward
trackEvents: (count, timeout) => trackEvents(count, timeout, null),

// ✅ Consumer accepts the full signature and decides for itself
var task = trackEvents(count, timeout, progress: null);
```

**Why:** The consumer owns its behavior. If it later starts using the parameter, only the consumer changes — no caller needs updating.

---

## Lambda Binding Strategy

### 31. Three-Bucket Rule for Lambda Binding

When composing delegates (lambdas) in a dependencies class or factory, every value falls into one of three buckets based on its **lifetime**. Choosing the wrong bucket leads to either stale closures or unnecessary parameters.

| Bucket                | Lifetime                                | Mechanism                          | Example                                                                 |
| --------------------- | --------------------------------------- | ---------------------------------- | ----------------------------------------------------------------------- |
| **Bake in**           | Immutable for the app's lifetime        | Close over in the lambda           | Config values, connection strings, container names, `CancellationToken` |
| **Pass as parameter** | Produced at runtime, different per call | Lambda parameter                   | `testRunId`, `serviceProcessId`, `testResult`                           |
| **Reader delegate**   | Can change during the app's lifetime    | `Func<T>` that reads current value | Feature flags, user preferences, dynamic settings                       |

**Bake in** — the value is known at construction time and will never change:

```csharp
// ✅ Good - DatabaseName and CancellationToken are fixed for the app's lifetime
PhasesToolbox.ClearDatabase clearDatabase = () => database.ClearDatabaseAsync(config.DatabaseName.Value, ct);

// ✅ Good - container name won't change mid-run
GetRabbitMqMetrics: () => dockerMonitor.GetMetricsAsync(config.RabbitMqContainerName.Value),
```

**Pass as parameter** — the value is produced during execution and varies per invocation:

```csharp
// ✅ Good - testRunId is generated at runtime, serviceProcessId comes from a previous phase
public delegate Task<Result<ProcessId, string>> RunSetup(Guid testRunId);
public delegate Task<Result<EventTestOutput, string>> RunEventTest(ProcessId serviceProcessId);

// ❌ Avoid - baking in a runtime value that doesn't exist yet
// (This would require constructing the delegate after the value is produced,
//  breaking the Configure → Build → Run separation)
public static async Task ExecuteAsync(FindServiceProcessId findServiceProcessId)
{
    var pid = await findServiceProcessId();
    var deps = EventTestPhase.BuildDependencies(pid, ...);  // pid baked into delegates
    await EventTestPhase.ExecuteAsync(deps);                 // too late — Build already ran
}
```

**Reader delegate** — the value may change between calls:

```csharp
// ✅ Good - if a setting could be toggled at runtime, read it each time
public delegate int GetMaxRetries();  // reads current value on each call

var deps = new Dependencies(
    GetMaxRetries: () => settingsStore.CurrentMaxRetries,
    ...);

// ❌ Avoid - baking in a mutable value (stale closure)
var maxRetries = settingsStore.CurrentMaxRetries;
var deps = new Dependencies(
    MaxRetries: maxRetries,  // snapshot — won't reflect later changes
    ...);
```

**Decision flowchart:**

1. **Does the value exist at construction time?**
    - No → it's a **lambda parameter** (produced at runtime)
    - Yes → continue to 2
2. **Can the value change after construction?**
    - Yes → use a **reader delegate** (`Func<T>` or named delegate)
    - No → **bake it in** (close over it)

**Current codebase examples:**

| Value                          | Bucket    | Where                                                                           |
| ------------------------------ | --------- | ------------------------------------------------------------------------------- |
| `config.DatabaseName`          | Bake in   | `TestOrchestrator.BuildDependencies` — closed over in `ClearDatabase` lambda    |
| `config.RabbitMqContainerName` | Bake in   | `ReportingPhase.BuildDependencies` — closed over in `GetRabbitMqMetrics` lambda |
| `CancellationToken`            | Bake in   | All phase delegates — closed over at construction                               |
| `testRunId`                    | Parameter | `RunSetup(Guid testRunId)` — generated at runtime                               |
| `serviceProcessId`             | Parameter | `RunEventTest(ProcessId serviceProcessId)` — output of Setup phase              |
| `testResult`                   | Parameter | `RunReporting(TestResult testResult)` — assembled from all phase outputs        |

**Why this matters:**

- **Baking in a runtime value** forces you to delay delegate construction, breaking the clean Configure → Build → Run separation (Guideline 29) (see [Delegates and Dependency Wiring](02-delegates-and-dependency-wiring.md))
- **Passing a fixed value as a parameter** clutters every call site with values that never change
- **Baking in a mutable value** creates stale closures that silently use outdated data
- **Using a reader delegate for an immutable value** adds unnecessary indirection

### 32. Context Record Pattern (Shared Mutable State)

When a class or function group has mutable state, centralize all mutable state in a single record. Pass the record explicitly to static functions. The value is **visibility** — all state that can change lives in one place.

Apply at the first sign of mutable state — consistency matters more than saving a record definition.

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

**Why mutable, not immutable?** When state is shared across concurrent tasks, returning a new record doesn't work — other threads still hold the old reference:

```csharp
// ❌ Broken — Thread B still holds the old reference
// Thread A:
ctx = ctx with { StreamingFailed = true };  // creates new record

// Thread B (still holds original ctx):
if (ctx.StreamingFailed) { ... }  // always false — different reference
```

Types like `ConcurrentBag<T>`, `TaskCompletionSource`, and `SemaphoreSlim` are inherently mutable — other code holds references to the original instances. Copying them into a new record would fork the state. For single-threaded pipelines where immutability IS possible, see Guideline 33.

**When to use:** Any class or function group that has mutable state — even a single field.

**When NOT to use:** Purely stateless functions (like `DatabaseCleaner`, `RabbitMqCleaner`) — no state to extract.

**Structure:**

- `sealed record` with constructor parameters for fixed identity (e.g., `ContainerName`)
- Mutable properties: `{ get; set; }` or self-initializing collections (`= new()`)
- No logic in the record — it's a state bag, not a service

**Relationship to other guidelines:**

- Extends **Guideline 2** (explicit parameters) from single values to state bundles (see [Core Architecture](01-core-architecture.md))
- Used by **Guideline 34** (thin shell) as the state extraction technique
- For sequential code, prefer **Guideline 33** (immutable state threading)

### 33. Immutable State Threading (Sequential Pipelines)

When state flows through a single-threaded pipeline — no concurrent access, one step at a time — return a **new** record from each step instead of mutating. The caller reassigns the variable; the record itself is never mutated. This is the FP fold/reduce pattern in C#.

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

- State is shared across concurrent tasks — use Guideline 32 (mutable context record)
- State contains inherently mutable types (`ConcurrentBag`, `TaskCompletionSource`) — these can't be copied meaningfully

**Structure:**

- `sealed record` with all properties in the constructor (positional record)
- All properties are immutable (no `{ get; set; }`)
- Functions return the record type (the new state), not `void`
- Caller uses `Aggregate` or `state = Function(state, input)` pattern

**Relationship to other guidelines:**

- Companion to **Guideline 32** — same idea (explicit state), different concurrency model
- Extends **Guideline 1** (static classes) — static functions that transform state (see [Core Architecture](01-core-architecture.md))
- Extends **Guideline 2** (explicit parameters) — state is an input AND an output (see [Core Architecture](01-core-architecture.md))

### 34. Thin Shell Pattern for Framework-Coupled Classes

Guidelines 1 and 2 (see [Core Architecture](01-core-architecture.md)) say "make it static" and "pass all dependencies explicitly." But some classes _can't_ be static — they inherit from framework base classes (`BackgroundService`, `DbContext`, `ControllerBase`). These classes accumulate mutable state fields, business logic methods, and lifecycle management in one file, making them hard to test and reason about.

**The pattern:** Extract everything out. The framework-inheriting class becomes a **thin shell** with three responsibilities only:

1. **Own the context** — a context record holding all mutable state (Guideline 32 or 33)
2. **Wire lifecycle** — connect framework hooks to static functions
3. **Expose the public API** — delegate to context or static functions

```csharp
// 1. Context record — all mutable state, no logic (Guideline 32)
internal sealed record MonitorContext(NonEmptyString ContainerName)
{
    public ConcurrentBag<DockerMetrics> CollectedMetrics { get; } = new();
    public TaskCompletionSource StartSignal { get; } = new();
    public bool StreamingFailed { get; set; }
}

// 2. Static operations — all logic, explicit parameters, no instance state
internal static class MonitoringOperations
{
    public static async Task RunStreamingLoopAsync(
        MonitorContext ctx,
        ContainerId initialContainerId,
        GetContainerIdDelegate getContainerId,
        StreamMetricsDelegate streamMetrics,
        ILogger logger,
        CancellationToken ct)
    {
        // All state access goes through ctx parameter
    }
}

// 3. Thin shell — owns context, wires lifecycle, no business logic
internal sealed class DockerMonitorService : BackgroundService
{
    private readonly MonitorContext _ctx;
    private readonly GetContainerIdDelegate _getContainerId;
    // ... other delegates ...

    public DockerMonitorService(NonEmptyString containerName, ...)
    {
        _ctx = new MonitorContext(containerName);
        // ... store delegates ...
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Lifecycle wiring only: wait for signal, resolve ID, delegate to static function
        var streamingTask = Task.Run(
            () => MonitoringOperations.RunStreamingLoopAsync(
                _ctx, containerId, _getContainerId, ...),
            stoppingToken);
        // ... await shutdown ...
    }

    public IReadOnlyCollection<DockerMetrics> GetCollectedMetrics() => _ctx.CollectedMetrics
        .OrderBy(m => m.Timestamp)
        .ToList()
        .AsReadOnly();
}
```

```csharp
// ❌ Avoid — framework class owns state, logic, and lifecycle together
internal sealed class DockerMonitorService : BackgroundService
{
    private readonly ConcurrentBag<DockerMetrics> _collectedMetrics = new();
    private readonly TaskCompletionSource _startSignal = new();
    private volatile bool _streamingFailed;
    private Task? _streamingTask;
    private CancellationTokenSource? _streamingCts;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 200+ lines mixing lifecycle management with business logic
    }

    private async Task RunStreamingLoopAsync(...) { /* business logic buried in instance method */ }
    private async Task<ConnectionState> ExecuteStreamingSessionAsync(...) { /* more logic */ }
    private async Task<ConnectionState> ExecuteReconnectAsync(...) { /* more logic */ }
}
```

**Same pattern for API wrappers:**

```csharp
// ✅ Good — static operations + delegates, no wrapper class
internal static class DockerOperations
{
    public static async Task<Result<string>> GetContainerIdAsync(
        DockerClient client, string containerName, CancellationToken ct) { ... }

    public static async IAsyncEnumerable<DockerMetrics> StreamMetricsAsync(
        DockerClient client, string containerId, ...) { ... }
}

// DI registers delegates that close over the shared client:
services.AddSingleton<GetContainerIdDelegate>(sp =>
    (name, ct) => DockerOperations.GetContainerIdAsync(sp.GetRequiredService<DockerClient>(), name, ct));

// ❌ Avoid — wrapper class holding client as field
internal sealed class DockerClientWrapper
{
    private readonly DockerClient _client;
    public async Task<Result<string>> GetContainerIdAsync(...) { ... }
}
```

**When to use:**

- A class inherits from a framework base class (`BackgroundService`, `ControllerBase`, `DbContext`)
- A class wraps an external client/SDK as instance state

**When NOT to use:**

- The class has no framework coupling — just make it static (Guideline 1) (see [Core Architecture](01-core-architecture.md))

**File organization:**

| File                          | Content                        |
| ----------------------------- | ------------------------------ |
| `MonitorContext.cs`           | Mutable state record           |
| `MonitoringOperations.cs`    | Static logic functions         |
| `DockerMonitorService.cs`    | Thin shell (lifecycle only)    |

**Relationship to other guidelines:**

- Applies **Guideline 32** (context record) or **Guideline 33** (immutable state) for the state extraction
- Extends **Guideline 1** (static classes) to cases where the class itself can't be static (see [Core Architecture](01-core-architecture.md))
- Applies **Guideline 2** (explicit parameters) — static functions take context + delegates, not fields (see [Core Architecture](01-core-architecture.md))
- Uses **Guideline 12** (named delegates) for the operations that the shell passes to static functions (see [Delegates and Dependency Wiring](02-delegates-and-dependency-wiring.md))
- Follows **Guideline 14** (interfaces at DI boundaries, delegates internally) — the shell wires delegates to static functions (see [Delegates and Dependency Wiring](02-delegates-and-dependency-wiring.md))
