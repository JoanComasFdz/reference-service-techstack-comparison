# 02-05. Static Class as Module (Co-located Dependencies)

A static class can serve as an **FP-style module** — owning its delegate definitions, a `Dependencies` record that bundles them, a factory to build them, and the execution method. This mirrors FP companion modules (e.g., F# allows a type and module to share the same name). In C#, the static class **is** the module — no separate builder class needed.

This improves discoverability, especially for delegates where IDE Ctrl+Click navigation doesn't work. Everything reads top-to-bottom: **what I need → how to bundle it → how to build it → what I do with it**.

**Naming:** Use the `Module` suffix for any static class that follows this pattern (delegates + Dependencies record + BuildDependencies factory + execution method). The suffix signals that this is a cohesive FP-style module — not a toolbox class, not a pure calculation class, not a single-function utility. The rest of the name describes the domain responsibility.

| Class | Why "Module" | Domain meaning |
|---|---|---|
| `SetupPhaseModule` | delegates + Dependencies + factory + ExecuteAsync | The module that implements the setup phase |
| `ApiTestPhaseModule` | delegates + Dependencies + factory + ExecuteAsync | The module that implements the API test phase |
| `TestOrchestrationModule` | delegates + Dependencies + factory + RunTestAsync | The module that orchestrates the full test run |
| `MonitoringModule` | context + Dependencies + static operations | The module that implements Docker container monitoring |
| `ProcessMonitorModule` | context + static operations | The module that implements process monitoring |
| `DockerStatsModule` | delegates + composition root | The module that implements Docker stats operations |
| `ConnectionModule` | value object + state machine + policy + constants | The module that owns Docker streaming connection lifecycle |

What does **not** get the `Module` suffix:

- `PhasesToolbox` — shared utility delegates, no Dependencies record, no execution method
- `TestReportBuilder` — single pure function, no delegates, no Dependencies
- `ProcessNameExtractor` — pure calculation class nested inside a module
- `ConnectionModule.StateMachine` — pure state transition logic, nested inside ConnectionModule

The suffix answers a question at a glance: "Is this a cohesive unit with its own delegates, dependency wiring, and execution — or is it something simpler?" It also mirrors FP language conventions where "module" is a first-class organizational concept (F# modules, Haskell modules, OCaml modules). In C# we don't have native modules, but the suffix makes the intent explicit.

```csharp
// ✅ Good - self-contained module: types → bundle → factory → execution
internal static class SetupPhaseModule
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

**`BuildDependencies` is always present, even when trivial.** If a module receives all its dependencies pre-composed (e.g., shared delegates passed in directly from the orchestration level), `BuildDependencies` may do nothing more than forward parameters into `new Dependencies(...)`. This is still correct — the factory slot must remain occupied to maintain the four-part structure and preserve the top-to-bottom reading contract. A module where one phase happens to need only shared delegates is an accident of that phase's current requirements, not an architectural signal to simplify. Removing or inlining `BuildDependencies` breaks pattern uniformity and makes that module look different from all others without good reason. This is explicitly exempt from [Guideline 01-09](../01-core-architecture/01-09-no-wrapper-functions.md) (No Wrapper Functions).

**File placement:** See [Guideline 05-06](../05-state-and-composition-patterns/05-06-thin-shell-pattern.md) for where Module files belong in the directory structure (`Internal/{Concept}Module.cs`) and the visibility-first file organization rules.

**When to use:**

- A static method has many delegate parameters (4+) that are always passed together
- The delegates are only used by this one consumer
- IDE navigation for delegates is important (no Ctrl+Click on delegate types)

**When NOT to use:**

- The factory needs to be called from multiple unrelated sites (keep it separate)
- The dependencies are shared across multiple consumers (use `PhasesToolbox` instead)
