# State and Composition Patterns

> Guidelines 05-01 through 05-06. For the full index and routing table, see [CODING_GUIDELINES.md](../CODING_GUIDELINES.md).

Advanced patterns for composing functions, binding lambdas, and managing state. Covers higher-order helpers, lambda binding lifetime buckets, mutable vs immutable state records, and the thin shell pattern for framework-coupled classes.

---

## Function Composition

### 05-01. Higher-Order Helper Functions for Structural Duplication

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

### 05-02. Behavioral Decisions Belong in the Consumer, Not the Caller

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

### 05-03. Three-Bucket Rule for Lambda Binding

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

- **Baking in a runtime value** forces you to delay delegate construction, breaking the clean Configure → Build → Run separation (Guideline 02-04) (see [Delegates and Dependency Wiring](02-delegates-and-dependency-wiring.md))
- **Passing a fixed value as a parameter** clutters every call site with values that never change
- **Baking in a mutable value** creates stale closures that silently use outdated data
- **Using a reader delegate for an immutable value** adds unnecessary indirection

### 05-04. Context Record Pattern (Shared Mutable State)

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

Types like `ConcurrentBag<T>`, `TaskCompletionSource`, and `SemaphoreSlim` are inherently mutable — other code holds references to the original instances. Copying them into a new record would fork the state. For single-threaded pipelines where immutability IS possible, see Guideline 05-05.

**When to use:** Any class or function group that has mutable state — even a single field.

**When NOT to use:** Purely stateless functions (like `DatabaseCleaner`, `RabbitMqCleaner`) — no state to extract.

**Structure:**

- `sealed record` with constructor parameters for fixed identity (e.g., `ContainerName`)
- Mutable properties: `{ get; set; }` or self-initializing collections (`= new()`)
- No logic in the record — it's a state bag, not a service

**Relationship to other guidelines:**

- Extends **Guideline 01-02** (explicit parameters) from single values to state bundles (see [Core Architecture](01-core-architecture.md))
- Used by **Guideline 05-06** (thin shell) as the state extraction technique
- For sequential code, prefer **Guideline 05-05** (immutable state threading)

### 05-05. Immutable State Threading (Sequential Pipelines)

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

**Why this works here but not in Guideline 05-04:** There is only one reference to the state. Reassigning `state = ...` updates the only copy. No other thread holds a stale reference.

**When to use:** Sequential processing (loops, pipelines, fold/reduce patterns) where state accumulates across steps but is only accessed by one thread.

**When NOT to use:**

- State is shared across concurrent tasks — use Guideline 05-04 (mutable context record)
- State contains inherently mutable types (`ConcurrentBag`, `TaskCompletionSource`) — these can't be copied meaningfully

**Structure:**

- `sealed record` with all properties in the constructor (positional record)
- All properties are immutable (no `{ get; set; }`)
- Functions return the record type (the new state), not `void`
- Caller uses `Aggregate` or `state = Function(state, input)` pattern

**Relationship to other guidelines:**

- Companion to **Guideline 05-04** — same idea (explicit state), different concurrency model
- Extends **Guideline 01-01** (static classes) — static functions that transform state (see [Core Architecture](01-core-architecture.md))
- Extends **Guideline 01-02** (explicit parameters) — state is an input AND an output (see [Core Architecture](01-core-architecture.md))

### 05-06. Thin Shell Pattern for Framework-Coupled Classes

Guidelines 01-01 and 01-02 (see [Core Architecture](01-core-architecture.md)) say "make it static" and "pass all dependencies explicitly." But some classes _can't_ be static — they inherit from framework base classes (`BackgroundService`, `DbContext`, `ControllerBase`). These classes accumulate mutable state fields, business logic methods, and lifecycle management in one file, making them hard to test and reason about.

**The pattern:** Extract everything out. The framework-inheriting class becomes a **thin shell** with three responsibilities only:

1. **Own the context** — a context record holding all mutable state (Guideline 05-04 or 05-05)
2. **Wire lifecycle** — connect framework hooks to static functions
3. **Expose the public API** — delegate to context or static functions

```csharp
// 1. Module — delegates, context record, static operations (Guideline 02-05)
internal static class DockerMonitorModule
{
    // Delegates
    public delegate void ReportDockerMonitorProgressDelegate(DockerMonitorPhaseInfo phaseInfo);
    public delegate Task StartDockerMonitoringDelegate(
        ReportDockerMonitorProgressDelegate reportProgress,
        CancellationToken ct = default);
    public delegate IReadOnlyCollection<DockerMetrics> GetDockerMetricsDelegate();

    // Context record — all mutable state, no logic (Guideline 05-04)
    internal sealed record MonitorContext(NonEmptyString ContainerName)
    {
        public ConcurrentBag<DockerMetrics> CollectedMetrics { get; } = new();
        public TaskCompletionSource StartSignal { get; } = new();
        public bool StreamingFailed { get; set; }
    }

    // Static operations — all logic, explicit parameters, no instance state
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

// 2. Thin shell — owns context, wires lifecycle, no business logic
internal sealed class DockerMonitorBackgroundService : BackgroundService
{
    private readonly DockerMonitorModule.MonitorContext _ctx;
    private readonly GetContainerIdDelegate _getContainerId;
    // ... other delegates ...

    public DockerMonitorBackgroundService(NonEmptyString containerName, ...)
    {
        _ctx = new DockerMonitorModule.MonitorContext(containerName);
        // ... store delegates ...
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Lifecycle wiring only: wait for signal, resolve ID, delegate to module
        var streamingTask = Task.Run(
            () => DockerMonitorModule.RunStreamingLoopAsync(
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

- The class has no framework coupling — just make it static (Guideline 01-01) (see [Core Architecture](01-core-architecture.md))

**File organization — visibility-first structure:**

Library projects (consumed by other projects via `<ProjectReference>`) use a visibility-first file structure. Terminal projects (CLI, web host, workers with `<OutputType>Exe</OutputType>`) organize by domain concern instead — they have no external consumers, so `Api.cs` and `Internal/` are not needed.

| Location | Contains | Visibility |
|------|---------|---------|
| `Api.cs` | All public types: delegates, enums, phase info records, data records, public utilities | `public` |
| `ServiceCollectionExtensions.cs` | DI registration (`Add{SliceName}()`) | `public` |
| `ValueObjects/` | Value objects with `Create()` factories and validation logic | `public` |
| `Internal/{Concept}Module.cs` | Context record, static operations, nested internal utilities | `internal` |
| `Internal/{Concept}BackgroundService.cs` | Lifecycle wiring only (thin shell) | `internal` |

**The rule:** Root files define the public contract; `Internal/` contains the implementation.

**Type visibility in `Internal/`:** Every type declaration (class, record, struct, enum, delegate) inside the `Internal/` directory MUST use the `internal` access modifier — never `public`. Members of those internal types (methods, properties, constructors) CAN be `public` when needed for access by other internal types or by the DI registration layer. A `public` method on an `internal` class is effectively internal to the assembly; the containing type's visibility governs external accessibility.

```csharp
// ✅ Good — internal type with public members
internal static class ProcessMonitorModule
{
    // Public method — callable by the internal BackgroundService shell
    public static async Task RunSamplingLoopAsync(MonitorContext ctx, ...) { ... }
}

internal sealed class ProcessMonitorBackgroundService : BackgroundService
{
    // Public method — wrapped by a public delegate in ServiceCollectionExtensions.cs
    public async Task StartMonitoringAsync(ProcessId processId, ...) { ... }
}

// ❌ Avoid — public type inside Internal/ directory
public static class ProcessMonitorModule    // WRONG: type itself must be internal
{
    public static async Task RunSamplingLoopAsync(...) { ... }
}
```

```
// ✅ Good — visibility-first structure
ProcessMonitoring/
├── Api.cs                               ← public: delegates, phase info, ProcessMetrics
├── ServiceCollectionExtensions.cs       ← public: DI registration
├── ValueObjects/SampleCount.cs          ← public: value object (has validation logic)
└── Internal/
    ├── ProcessMonitorModule.cs          ← internal: context record, static operations, ProcessCpuCalculator, ProcessNameExtractor
    └── ProcessMonitorBackgroundService.cs ← internal: BackgroundService shell

// ❌ Avoid — flat at root, can't tell public from internal
ProcessMonitoring/
├── ProcessMonitorModule.cs              ← internal (but has public nested delegates?)
├── ProcessMonitorBackgroundService.cs   ← internal
├── ProcessMetrics.cs                    ← public
├── ProcessCpuCalculator.cs              ← internal (mixed in with public files)
├── ProcessNameExtractor.cs              ← public
├── ServiceCollectionExtensions.cs       ← public
└── ValueObjects/SampleCount.cs          ← public

// ❌ Avoid — one-type-per-file split in subdirectory
ProcessMonitoring/
└── Monitoring/
    ├── ProcessMonitoringDelegates.cs     ← 30 lines, 4 type declarations
    ├── MonitorContext.cs                 ← 17 lines, 1 record
    ├── ProcessMonitorPhaseInfo.cs        ← 131 lines, 2 enums + 1 record struct
    ├── MonitoringOperations.cs           ← 182 lines, 1 static class
    ├── PhaseReporting.cs                 ← 51 lines, 1 static class
    └── ProcessMonitorService.cs          ← 216 lines, shell
```

**Api.cs reading order** (matches Guideline 02-05): delegates → phase info (enums + record struct) → data records → public utilities. One file tells the complete public API story.

**Namespace convention:**
- `PerformanceTester.{SliceName}` — public contract (`Api.cs`, `ServiceCollectionExtensions.cs`, `ValueObjects/`)
- `PerformanceTester.{SliceName}.Internal` — implementation (`Internal/` directory)

Consumers only ever `using PerformanceTester.{SliceName};`, never `.Internal`.

**Combining with Guideline 02-05 (static class as module):** The module file lives in `Internal/` and follows the same co-location principle — context record, static operations, and small internal utilities nested inside a single `internal static class`. The shell gets its own file because it inherits from a framework base class.

**Internal module nesting rule:** Internal types (context record, internal utilities) belong **nested inside** the module class. Public types (delegates, phase info, data records) belong in `Api.cs` as top-level types — they cannot be nested inside an `internal static class` and remain accessible to other projects.

```csharp
// ✅ Good — Api.cs has standalone public types, module has nested internal types

// Api.cs (at project root)
namespace PerformanceTester.ProcessMonitoring;

public delegate void ReportProcessMonitorProgressDelegate(ProcessMonitorPhaseInfo phaseInfo);
public delegate Task StartProcessMonitoringDelegate(ProcessId processId, ...);
public readonly record struct ProcessMonitorPhaseInfo(...) { ... }
public record ProcessMetrics(...);

// Internal/ProcessMonitorModule.cs
namespace PerformanceTester.ProcessMonitoring.Internal;

internal static class ProcessMonitorModule
{
    internal sealed record MonitorContext { ... }          // nested — only used internally
    public static async Task RunSamplingLoopAsync(...) { ... }  // called by shell
    private static bool CollectSample(...) { ... }
    private static double SampleCpu(MonitorContext ctx, Process process) { ... }
}

// ❌ Avoid — public delegates nested inside internal class (forces class to be public)
namespace PerformanceTester.ProcessMonitoring;

internal static class ProcessMonitorModule  // must become public for delegates to be accessible
{
    public delegate void ReportProcessMonitorProgressDelegate(...);  // nested public in internal = inaccessible
    internal sealed record MonitorContext { ... }
}
```

**When the module file grows too large:** If the co-located module exceeds ~500 lines, extract the context record as the first split point. The reading order (delegates → records → operations) stays intact in the module file.

**When `Internal/` needs subfolders:** Only add subfolders inside `Internal/` when a slice has genuinely distinct subsystems. For example, DockerMonitoring has `Internal/ConnectionModule.cs`, `Internal/StatsModule.cs`, and `Internal/MonitoringModule.cs` because connection management, Docker API stats, and monitoring orchestration are separate concerns. Keep the structure flat unless organic complexity demands otherwise.

**Relationship to other guidelines:**

- Applies **Guideline 05-04** (context record) or **Guideline 05-05** (immutable state) for the state extraction
- Extends **Guideline 01-01** (static classes) to cases where the class itself can't be static (see [Core Architecture](01-core-architecture.md))
- Applies **Guideline 01-02** (explicit parameters) — static functions take context + delegates, not fields (see [Core Architecture](01-core-architecture.md))
- Uses **Guideline 02-01** (named delegates) for the operations that the shell passes to static functions (see [Delegates and Dependency Wiring](02-delegates-and-dependency-wiring.md))
- Follows **Guideline 02-03** (interfaces at DI boundaries, delegates internally) — the shell wires delegates to static functions (see [Delegates and Dependency Wiring](02-delegates-and-dependency-wiring.md))
