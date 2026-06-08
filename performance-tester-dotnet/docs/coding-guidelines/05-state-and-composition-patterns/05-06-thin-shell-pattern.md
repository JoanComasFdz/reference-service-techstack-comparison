# 05-06. Thin Shell Pattern for Framework-Coupled Classes

[Guideline 01-01](../01-core-architecture/01-01-static-classes.md) and [Guideline 01-02](../01-core-architecture/01-02-explicit-parameters.md) say "make it static" and "pass all dependencies explicitly." But some classes _can't_ be static — they inherit from framework base classes (`BackgroundService`, `DbContext`, `ControllerBase`). These classes accumulate mutable state fields, business logic methods, and lifecycle management in one file, making them hard to test and reason about.

**The pattern:** Extract everything out. The framework-inheriting class becomes a **thin shell** with three responsibilities only:

1. **Own the context** — a context record holding all mutable state ([Guideline 05-04](05-04-context-record-pattern.md) or [Guideline 05-05](05-05-immutable-state-threading.md))
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

- The class has no framework coupling — just make it static ([Guideline 01-01](../01-core-architecture/01-01-static-classes.md))

**File organization — visibility-first structure:**

Library projects (consumed by other projects via `<ProjectReference>`) use a visibility-first file structure. Terminal projects (CLI, web host, workers with `<OutputType>Exe</OutputType>`) organize by domain concern instead — they have no external consumers, so `Api.cs` and `Internal/` are not needed.

| Location | Contains | Visibility |
|------|---------|---------|
| `Api.cs` | All public types: delegates, enums, phase info records, data records, public utilities | `public` |
| `ServiceCollectionExtensions.cs` | DI registration (`Add{SliceName}()`) | `public` |
| `ValueObjects/` | Value objects with `Create()` factories and validation logic | `public` |
| `Internal/{Concept}Module.cs` | Context record, static operations, nested internal utilities (the `Module` suffix is defined by [Guideline 02-05](../02-delegates-and-dependency-wiring/02-05-static-class-as-module.md)) | `internal` |
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

**Foundational library exception:** Not all library projects need the `Api.cs` + `Internal/` structure. **Foundational libraries** — projects where nearly everything is public surface with no internal implementation to hide — use a simpler layout. Examples: a `Functional` project offering `Result<T, E>`, `Option<T>`, and extension methods; a pure utility library with standalone public types.

**How to tell the difference:** If the project would have an `Api.cs` that merely re-lists every file in the project, and an `Internal/` folder that is empty or trivial, it's a foundational library. The types themselves _are_ the entire offering — each with its own validation, factories, or extension methods.

These are still library projects (consumed via `<ProjectReference>`), not terminal projects. The distinction is between **slice-oriented libraries** (public contract + hidden implementation → `Api.cs` + `Internal/`) and **foundational libraries** (all contract, no hidden implementation → one type per file at root).

```
// ✅ Good — foundational library, all public surface, no internal implementation
 PerformanceTester.Functional/
├── Result.cs                  ← public: Result<T, E> + factory methods
├── Option.cs                  ← public: Option<T> + factory methods
├── Unit.cs                    ← public: Unit type
└── Extensions/
    ├── ResultExtensions.cs    ← public: LINQ-style extensions for Result
    └── OptionExtensions.cs    ← public: LINQ-style extensions for Option

// ❌ Avoid — forcing slice-oriented structure onto a foundational library
PerformanceTester.Functional/
├── Api.cs                     ← duplicates what the file listing already shows
├── Internal/                  ← empty or trivial (nothing to hide)
├── Result.cs
├── Option.cs
└── Unit.cs
```

**Api.cs reading order** (matches Guideline 02-05): delegates → phase info (enums + record struct) → data records → public utilities. One file tells the complete public API story.

**Namespace convention:**
- `PerformanceTester.{SliceName}` — public contract (`Api.cs`, `ServiceCollectionExtensions.cs`, `ValueObjects/`)
- `PerformanceTester.{SliceName}.Internal` — implementation (`Internal/` directory)

Consumers only ever `using PerformanceTester.{SliceName};`, never `.Internal`.

**Combining with [Guideline 02-05](../02-delegates-and-dependency-wiring/02-05-static-class-as-module.md) (static class as module):** The module file lives in `Internal/` and follows the same co-location principle — context record, static operations, and small internal utilities nested inside a single `internal static class`. The shell gets its own file because it inherits from a framework base class. **Module naming** is owned by [Guideline 02-05](../02-delegates-and-dependency-wiring/02-05-static-class-as-module.md) — it defines which classes qualify for the `Module` suffix. The file name mirrors the class name: if the class is `SetupPhaseModule`, the file is `SetupPhaseModule.cs`. This guideline (05-06) specifies WHERE the file goes (`Internal/`), not what earns the `Module` name.

**Internal module nesting rule:** Internal types (context record, internal utilities) belong **nested inside** the module class. Public types (delegates, phase info, data records) belong in `Api.cs` as top-level types — they cannot be nested inside an `internal static class` and remain accessible to other projects.

**Dunet `[Union]` exception:** Types decorated with `[Union]` cannot be nested inside a module class — even an `internal` one. Dunet's source generator emits `public` extension methods (e.g., `Match`, `MatchAsync`) at namespace level that reference the union type in their signatures. Nesting the union inside an `internal` class causes **CS0051** (inconsistent accessibility). Place `[Union]` types at namespace level in the same module file, with a `<see cref="...Module"/>` doc comment linking them back. They are logically part of the module but structurally must remain top-level. See also [Guideline 03-01](../03-error-handling/03-01-result-over-exceptions.md) placement constraint.

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

// ❌ Avoid — Dunet [Union] nested inside module class (CS0051: inconsistent accessibility)
namespace PerformanceTester.DockerMonitoring.Internal;

internal static class ConnectionModule
{
    [Union]
    internal partial record ConnectionState  // Dunet generates public Match extensions referencing this type
    {
        partial record Connecting(...);      // source-generated public methods can't see this
    }
}

// ✅ Good — Dunet [Union] at namespace level, non-Dunet types nested in module
namespace PerformanceTester.DockerMonitoring.Internal;

/// <remarks>Part of <see cref="ConnectionModule"/>
/// (kept at namespace level for Dunet source-generator compatibility).</remarks>
[Union]
internal partial record ConnectionState
{
    partial record Connecting(...);
}

internal static class ConnectionModule
{
    internal sealed record AttemptCount : NonNegativeInt { ... }    // nested — not a [Union]
    internal static class StateMachine { ... }                      // nested — not a [Union]
    internal static class StreamingConstants { ... }                // nested — not a [Union]
}
```

**When the module file grows too large:** If the co-located module exceeds ~500 lines, extract the context record as the first split point. The reading order (delegates → records → operations) stays intact in the module file.

**When `Internal/` needs subfolders:** Only add subfolders inside `Internal/` when a slice has genuinely distinct subsystems. For example, DockerMonitoring has `Internal/ConnectionModule.cs`, `Internal/DockerStatsModule.cs`, and `Internal/MonitoringModule.cs` because connection management, Docker API stats, and monitoring orchestration are separate concerns. Keep the structure flat unless organic complexity demands otherwise.

**Relationship to other guidelines:**

- Applies **[Guideline 05-04](05-04-context-record-pattern.md)** (context record) or **[Guideline 05-05](05-05-immutable-state-threading.md)** (immutable state) for the state extraction
- Extends **[Guideline 01-01](../01-core-architecture/01-01-static-classes.md)** (static classes) to cases where the class itself can't be static
- Applies **[Guideline 01-02](../01-core-architecture/01-02-explicit-parameters.md)** (explicit parameters) — static functions take context + delegates, not fields
- Uses **[Guideline 02-01](../02-delegates-and-dependency-wiring/02-01-named-delegates.md)** (named delegates) for the operations that the shell passes to static functions
- Follows **[Guideline 02-03](../02-delegates-and-dependency-wiring/02-03-interfaces-vs-delegates.md)** (interfaces at DI boundaries, delegates internally) — the shell wires delegates to static functions
