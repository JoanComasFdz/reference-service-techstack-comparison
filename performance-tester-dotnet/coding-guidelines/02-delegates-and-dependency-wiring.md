# Delegates and Dependency Wiring

> Guidelines 12-14, 29-30, 35. For the full index and routing table, see [CODING_GUIDELINES.md](../CODING_GUIDELINES.md).

This document covers the complete delegate-based dependency system: when to use named delegates vs interfaces, how to compose dependencies, the module pattern for co-locating delegates with their consumers, and naming conventions.

---

## Function-Typed Dependencies (Named Delegates)

When Guideline 10 says "Composition Over Interfaces," the natural question is: _what replaces the interface?_ For single-operation dependencies, the answer is a **named delegate**. This section covers when to use delegates, how to name them, and how they coexist with interfaces at DI boundaries.

### 12. Use Named Delegates for Single-Operation Dependencies

When a dependency is a single operation (one method), use a named `delegate` instead of an interface. Lighter than an interface, more descriptive than raw `Action<T>`/`Func<T>`.

```csharp
// ✅ Good - named delegate for a single operation
public delegate void ReportApiLoadProgress(ApiLoadProgress apiLoadProgress);

// ✅ Good - named delegate with richer signature
public delegate Task<ApiLoadTestResult> StartApiLoadTest(
    string targetUrl,
    TimeSpan duration,
    int virtualUsers,
    ReportApiLoadProgress progress,
    int maxConsecutiveFailures,
    string? scriptDirectory);

// ❌ Avoid - single-method interface (ceremony without benefit)
public interface IApiLoadProgressReporter
{
    void Report(ApiLoadProgress progress);
}

// ❌ Avoid - raw Action<T> that loses semantic meaning
public static async Task ExecuteAsync(Action<ApiLoadProgress> progress) { ... }
```

**When to use:**

- The dependency is a single operation, not a family of related operations
- The caller only needs to supply one behavior

**When NOT to use:**

- The dependency has multiple related methods that change together → use an interface
- The dependency needs DI container registration at a slice boundary → use an interface (see Guideline 14)

### 13. Prefer Named Delegates Over `Action<T>` / `Func<T>`

A named delegate communicates intent at the type level. This extends **Guideline 4** (see [Core Architecture](01-core-architecture.md)) — Descriptive Names to function-typed parameters.

```csharp
// ✅ Good - name says what it does
public delegate void ReportApiLoadProgress(ApiLoadProgress apiLoadProgress);
public delegate Task<Result<Unit, ClearDatabaseError>> ClearDatabase();
public delegate Task<Result<Unit, string>> ClearAllQueues();
public delegate Task<PublishMetrics> PublishEvents(int eventCount);

// ❌ Avoid - only says the shape, not the intent
Action<ApiLoadProgress>              // Could be anything that takes progress
Func<Task<Result<Unit, string>>>     // Could be any async operation
```

**Where to define the delegate:**

- **Next to its data type** if shared across callers (e.g., `ReportApiLoadProgress` next to `ApiLoadProgress` in `ApiLoadProgress.cs`)
- **Inside the consumer** if only used by one caller (e.g., `StartApiLoadTest` inside `ApiTestPhase`)

### 14. Interfaces at DI Boundaries, Delegates for Internal Wiring

Interfaces and delegates serve different layers. Use both, but in the right place:

| Layer                                        | Mechanism      | Example                                                              |
| -------------------------------------------- | -------------- | -------------------------------------------------------------------- |
| **DI boundary** (slice API)                  | Interface      | `IApiLoadTester`, `IRabbitMQ`                                        |
| **DI boundary** (single operation)           | Named delegate | `ClearDatabase`, `FindServiceProcessId`                              |
| **Internal wiring** (between static classes) | Named delegate | `StartApiLoadTest`                                                   |
| **Orchestrator**                             | Lambda adapter | Closes over `CancellationToken`, adapts interface/delegate → delegate |

```csharp
// ✅ Good - orchestrator adapts interface to delegate via lambda
var (apiResult, apiTestStartTime, apiTestEndTime) = await ApiTestPhase.ExecuteAsync(
    configuration,
    (url, duration, vus, apiProgress, maxFail, dir) =>
        _apiLoadTester.StartTestAsync(url, duration, vus, apiProgress, maxFail, dir, cancellationToken),
    progress,
    _logger);

// ✅ Good - shared delegates created once, reused across phases
PhasesToolbox.ClearDatabase clearDatabase = () =>
    clearDatabaseAsync(configuration.DatabaseName.Value, cancellationToken);

// ❌ Avoid - phase class depending directly on DI interface
public static async Task ExecuteAsync(IApiLoadTester apiLoadTester, ...) { ... }
// Couples the phase to the DI interface; the phase doesn't need the full interface
```

**Why the orchestrator adapts:**

- Phase classes stay decoupled from DI interfaces (testable with simple lambdas)
- `CancellationToken` belongs to the orchestrator, not the phase — the lambda closes over it
- The phase only sees the exact operation it needs, not the full interface surface

> **Evolution note:** Single-method interfaces at DI boundaries (e.g., the former `IDatabase`) are being migrated to named delegates as the functional approach extends beyond Orchestration. The interface-at-boundary rule applies primarily to multi-method contracts. For single-operation contracts, prefer a named delegate even at the DI boundary.

---

## 29. Minimize Interface Reach with Dependency Composition

Classes should depend on **pre-composed capabilities**, not on the interfaces or individual operations behind them. Interfaces are a DI registration concern — contain them in a **dependencies class**: a static factory that resolves interfaces and produces bound delegates at the right abstraction level. Consumers receive delegates matching their actual abstraction level.

```csharp
// ✅ Good — dependencies class composes phases, orchestrator sees only phase delegates
internal static class OrchestratorDependencies
{
    public static OrchestratorDeps Build(
        IServiceProvider services, Config config, CancellationToken ct) => new(
        RunSetup: (testRunId) => SetupPhase.ExecuteAsync(testRunId,
            clearDb: () => services.GetRequiredService<IDatabase>().ClearAsync(config.Db, ct),
            ...),
        RunProcess: () => ProcessPhase.ExecuteAsync(
            start: () => services.GetRequiredService<IProcessRunner>().StartAsync(ct),
            ...));
}

// Consumer — knows only about phases, not their internals
internal static class Orchestrator
{
    public static async Task<Result<Report, Error>> RunAsync(OrchestratorDeps deps)
    {
        var setup = await deps.RunSetup(Guid.NewGuid());
        var result = await deps.RunProcess();
        ...
    }
}

// ❌ Avoid — consumer depends on every individual operation
public class Orchestrator(IDatabase db, IProcessRunner runner, IEventPublisher publisher)
{
    // Knows about clearing databases, starting processes, publishing events...
    // Should only know about phases.
}
```

**Why:**

- Classes should know about their dependencies at their abstraction level, not every small operation
- Dependency composition classes centralize plumbing (interface resolution, config binding, CT binding)
- Consumers become testable with simple lambdas at the right granularity
- Adding a new operation inside a phase doesn't change the orchestrator

**The pattern: Configure → Build → Run**

1. **Configure:** Register interfaces in DI (`AddInfrastructure()`, `AddEventPublishing()`, etc.)
2. **Build:** Dependencies class resolves interfaces, composes them into capability-level delegates
3. **Run:** Consumer calls delegates with zero knowledge of interfaces or internal operations

**When to use:**

- Any class that coordinates multiple components (orchestrators, pipelines)
- Any class where you use only specific methods from broader interfaces
- Any static class that needs "injected" capabilities

**When NOT to use:**

- Inside the dependencies class itself (it must see interfaces to compose them)
- Leaf classes that genuinely work at the operation level (the phases themselves)

### 30. Static Class as Module (Co-located Dependencies)

A static class can serve as an **FP-style module** — owning its delegate definitions, a `Dependencies` record that bundles them, a factory to build them, and the execution method. This mirrors FP companion modules (e.g., F# allows a type and module to share the same name). In C#, the static class **is** the module — no separate builder class needed.

This improves discoverability, especially for delegates where IDE Ctrl+Click navigation doesn't work. Everything reads top-to-bottom: **what I need → how to bundle it → how to build it → what I do with it**.

```csharp
// ✅ Good - self-contained module: types → bundle → factory → execution
internal static class SetupPhase
{
    // 1. Delegate definitions (what I need)
    public delegate Task<Result<int, string>> FindServiceProcessId();
    public delegate bool IsMonitoringStarted();
    public delegate Task StartMonitoring();
    public delegate Task WarmupDockerApi();
    public delegate Task ConnectEventPublisher();

    // 2. Dependencies record (bundle of what I need)
    public record Dependencies(
        FindServiceProcessId FindServiceProcessId,
        IsMonitoringStarted IsMonitoringStarted,
        StartMonitoring StartMonitoring,
        WarmupDockerApi WarmupDockerApi,
        PhasesToolbox.ClearDatabase ClearDatabase,
        PhasesToolbox.ClearAllQueues ClearAllQueues,
        ConnectEventPublisher ConnectEventPublisher);

    // 3. Factory (how to build what I need from DI)
    public static Dependencies BuildDependencies(
        IServiceProvider services,
        PhasesToolbox.ClearDatabase clearDatabase,
        PhasesToolbox.ClearAllQueues clearAllQueues,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var findServiceProcessId = services.GetRequiredService<FindServiceProcessId>();
        // ... resolve and compose ...
        return new Dependencies(...);
    }

    // 4. Execution (what I do with it)
    public static async Task<Result<int, string>> ExecuteAsync(
        Guid testRunId,
        Dependencies deps,
        ILogger logger)
    {
        var pidResult = await deps.FindServiceProcessId();
        // ...
    }
}

// ❌ Avoid - separate file/class just for building dependencies
internal static class SetupPhaseDependencies
{
    public static RunSetup Build(IServiceProvider services, ...) { ... }
}
```

**Why:** In FP languages, a module contains both its types and its functions — there's no separate "builder" concept. C# static classes serve the same role. Co-locating definitions, bundling, construction, and execution gives a top-to-bottom reading flow and eliminates file-hopping.

**When to use:**

- A static method has many delegate parameters (4+) that are always passed together
- The delegates are only used by this one consumer
- IDE navigation for delegates is important (no Ctrl+Click on delegate types)

**When NOT to use:**

- The factory needs to be called from multiple unrelated sites (keep it separate)
- The dependencies are shared across multiple consumers (use `PhasesToolbox` instead)

---

### 35. Delegate Suffix Convention

All named delegate types must end with `Delegate`. This makes delegate types instantly recognizable as function types — distinct from classes, interfaces, and methods.

```csharp
// ✅ Good - "Delegate" suffix identifies these as function types
internal delegate Task<Result<string, DockerError>> GetContainerIdDelegate(
    string containerName,
    CancellationToken ct = default);

internal delegate IAsyncEnumerable<DockerMetrics> StreamMetricsDelegate(
    string containerId,
    NonEmptyString containerName,
    CancellationToken ct);

public delegate Task<Result<Unit, ClearDatabaseError>> ClearDatabaseDelegate(
    CancellationToken cancellationToken = default);

// ❌ Avoid - no suffix, ambiguous type
public delegate Task<Result<Unit, ClearDatabaseError>> ClearDatabase(
    CancellationToken cancellationToken = default);

public delegate Task<Result<ProcessId, string>> FindServiceProcessId(
    Port port,
    CancellationToken cancellationToken);
```

**Why the suffix matters:**

The suffix solves three problems:

1. **Type vs parameter ambiguity in records.** Without the suffix, positional record parameters repeat the type name verbatim — `RunSetup RunSetup`. Readers must check context to know which is the type and which is the parameter. The suffix makes it instant:

```csharp
// ❌ Avoid - type and parameter names are identical
public record Dependencies(
    RunSetup RunSetup,
    RunWarmup RunWarmup,
    FindServiceProcessId FindServiceProcessId,
    ClearDatabase ClearDatabase);

// ✅ Good - type is clearly distinguished from parameter
public record Dependencies(
    RunSetupDelegate RunSetup,
    RunWarmupDelegate RunWarmup,
    FindServiceProcessIdDelegate FindServiceProcessId,
    ClearDatabaseDelegate ClearDatabase);
```

2. **Readability at usage sites.** When scanning code, `ClearDatabaseDelegate clearDatabase` immediately communicates "this is a function I can call." Without the suffix, `ClearDatabase clearDatabase` looks like a constructor call or variable declaration of a class instance.

3. **IDE discoverability.** Searching for "Delegate" surfaces all function types. Without the suffix, delegate types are mixed in with classes and interfaces in search results.

**Naming the parameter:** The parameter name drops the suffix and uses camelCase — the suffix is on the TYPE, not the variable:

```csharp
// ✅ Good - type has suffix, parameter does not
public static async Task ExecuteAsync(
    ClearDatabaseDelegate clearDatabase,
    FindServiceProcessIdDelegate findServiceProcessId,
    ILogger logger)
{
    var result = await clearDatabase(ct);
    var pid = await findServiceProcessId(port, ct);
}

// ❌ Avoid - suffix on parameter name (redundant noise)
public static async Task ExecuteAsync(
    ClearDatabaseDelegate clearDatabaseDelegate,
    FindServiceProcessIdDelegate findServiceProcessIdDelegate,
    ILogger logger)
```

> **Evolution note:** All delegate declarations across the codebase have been migrated to use the `Delegate` suffix (e.g., `ClearDatabaseDelegate`, `RunSetupDelegate`). Examples in earlier guidelines (12, 13, 14, 30, 31) may still show unsuffixed names for brevity; the production code is the authoritative reference.

**Applies to:** All `delegate` type declarations — `public`, `internal`, and `private`. No exceptions.

**Relationship to other guidelines:**

- Constrains **Guideline 12** (named delegates) — Guideline 12 says _when_ to use delegates; this says _how to name_ them
- Extends **Guideline 13** (named over Action/Func) — Guideline 13 says use a descriptive name; the `Delegate` suffix is part of that name
- Affects **Guideline 30** (static class as module) — Dependencies records benefit most from the suffix (type vs parameter disambiguation)
