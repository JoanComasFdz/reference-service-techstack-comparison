# 05-03. Three-Bucket Rule for Lambda Binding

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

- **Baking in a runtime value** forces you to delay delegate construction, breaking the clean Configure → Build → Run separation ([Guideline 02-04](../02-delegates-and-dependency-wiring/02-04-dependency-composition.md))
- **Passing a fixed value as a parameter** clutters every call site with values that never change
- **Baking in a mutable value** creates stale closures that silently use outdated data
- **Using a reader delegate for an immutable value** adds unnecessary indirection
