# 02-03. Interfaces at DI Boundaries, Delegates for Internal Wiring

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
