# Teardown / CleanupResources Separation Design

**Date:** 2026-02-14
**Status:** Approved

## Problem

`TeardownPhaseDependencies.Build()` returns a tuple of two delegates:

```csharp
var (runTeardown, cleanupResources) = TeardownPhaseDependencies.Build(services, logger, ct);
```

- `RunTeardown` — cancellable phase delegate (normal flow)
- `CleanupResources` — non-cancellable best-effort delegate (finally block)

Both call the same `TeardownPhase.ExecuteAsync()` logic, differing only in the bound `CancellationToken`. This creates two issues:

1. **Responsibility leak:** `CleanupResources` is an orchestrator lifecycle concern, not a phase concern. No other phase dependency produces orchestrator-level delegates.
2. **Pattern break:** Every other phase dependency returns a single delegate. Teardown returns a tuple.

## Design Decision

`CleanupResources` will always remain semantically identical to `RunTeardown` (same operations, just non-cancellable).

## Solution

Defer `CancellationToken` binding by making the teardown delegate accept it as a parameter.

### TeardownPhaseDependencies

Define a `Teardown` delegate type that accepts a `CancellationToken`. The delegate lives here (not in `PhasesToolbox`) because it is not a cross-phase shared operation.

`Build()` returns a single `Teardown` delegate with services and logger pre-bound, but CT left as a caller-supplied parameter. The CT parameter is also removed from `Build()` itself since it's no longer needed.

```csharp
internal static class TeardownPhaseDependencies
{
    public delegate Task<Result<Unit, string>> Teardown(CancellationToken ct);

    public static Teardown Build(IServiceProvider services, ILogger logger)
    {
        var host = services.GetRequiredService<IHost>();
        var eventPublisher = services.GetRequiredService<IEventPublisher>();

        return (ct) => TeardownPhase.ExecuteAsync(
            disconnectEventPublisher: () => eventPublisher.DisconnectAsync(ct),
            stopMonitoring: () => host.StopAsync(ct),
            logger);
    }
}
```

### TestOrchestratorDependencies

The composition root binds the CT for both use cases:

```csharp
var teardown = TeardownPhaseDependencies.Build(services, logger);

return new OrchestratorDeps(
    ...
    RunTeardown: () => teardown(ct),
    CleanupResources: () => teardown(CancellationToken.None),
    ...);
```

### What doesn't change

- `OrchestratorDeps` record (still has both `RunTeardown` and `CleanupResources` fields)
- `RunTeardown` and `CleanupResources` delegate types in `OrchestratorDelegates.cs`
- `TeardownPhase.ExecuteAsync()` logic
- `TestOrchestrator` usage of both delegates
- Tests

## Benefits

- **Single return value** from `TeardownPhaseDependencies.Build()` (consistent with all other phases)
- **CT binding at composition root** where cancellation policy decisions belong
- **No double DI resolution** (single `Build()` call)
- **Delegate type co-located** with its producer (not in PhasesToolbox where it doesn't belong)
