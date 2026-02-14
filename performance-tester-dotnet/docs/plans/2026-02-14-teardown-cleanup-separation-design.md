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

---

## Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Move CT binding responsibility from TeardownPhaseDependencies to TestOrchestratorDependencies.

**Architecture:** TeardownPhaseDependencies defines a `Teardown` delegate accepting CT and returns it from `Build()`. The composition root binds CT for both RunTeardown and CleanupResources.

**Tech Stack:** C# / .NET 9

---

### Task 1: Refactor TeardownPhaseDependencies to return single delegate

**Files:**
- Modify: `performance-tester-dotnet/src/PerformanceTester.Orchestration/Phases/TeardownPhaseDependencies.cs`

**Step 1: Replace file contents**

Replace the entire `TeardownPhaseDependencies` class with:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.EventPublishing;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Builds a <see cref="Teardown"/> delegate from DI-resolved interfaces.
/// The returned delegate accepts a <see cref="CancellationToken"/> so the caller
/// can control cancellation policy (cancellable for normal flow, non-cancellable for cleanup).
/// </summary>
internal static class TeardownPhaseDependencies
{
    /// <summary>
    /// Runs teardown operations (disconnect publisher, stop monitoring) with caller-supplied cancellation.
    /// </summary>
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

**Step 2: Build to verify compilation**

Run: `dotnet build performance-tester-dotnet/src/PerformanceTester.Orchestration`
Expected: Build failure in `TestOrchestratorDependencies.cs` (still calling old signature). This confirms the change is wired.

---

### Task 2: Update TestOrchestratorDependencies to bind CT at composition root

**Files:**
- Modify: `performance-tester-dotnet/src/PerformanceTester.Orchestration/TestOrchestratorDependencies.cs`

**Step 1: Replace the teardown call and OrchestratorDeps construction**

Change lines 43-52 from:

```csharp
        var (runTeardown, cleanupResources) = TeardownPhaseDependencies.Build(services, logger, ct);

        return new OrchestratorDeps(
            RunSetup: SetupPhaseDependencies.Build(services, clearDatabase, clearAllQueues, config, logger, ct),
            RunWarmup: WarmupPhaseDependencies.Build(trackEvents, publishEvents, clearDatabase, clearAllQueues, config, logger, ct),
            RunEventTest: EventTestPhaseDependencies.Build(services, trackEvents, publishEvents, config, logger, ct),
            RunApiTest: ApiTestPhaseDependencies.Build(services, config, logger, ct),
            RunTeardown: runTeardown,
            RunReporting: ReportingPhaseDependencies.Build(services, config, logger, ct),
            CleanupResources: cleanupResources);
```

To:

```csharp
        var teardown = TeardownPhaseDependencies.Build(services, logger);

        return new OrchestratorDeps(
            RunSetup: SetupPhaseDependencies.Build(services, clearDatabase, clearAllQueues, config, logger, ct),
            RunWarmup: WarmupPhaseDependencies.Build(trackEvents, publishEvents, clearDatabase, clearAllQueues, config, logger, ct),
            RunEventTest: EventTestPhaseDependencies.Build(services, trackEvents, publishEvents, config, logger, ct),
            RunApiTest: ApiTestPhaseDependencies.Build(services, config, logger, ct),
            RunTeardown: () => teardown(ct),
            RunReporting: ReportingPhaseDependencies.Build(services, config, logger, ct),
            CleanupResources: async () => await teardown(CancellationToken.None));
```

**Step 2: Build to verify compilation**

Run: `dotnet build performance-tester-dotnet/src/PerformanceTester.Orchestration`
Expected: PASS with zero warnings.

---

### Task 3: Run tests to verify no behavioral change

**Step 1: Run orchestration integration tests**

Run: `dotnet test performance-tester-dotnet/src/PerformanceTester.Orchestration.IntegrationTests --logger "console;verbosity=detailed"`
Expected: All tests PASS.

**Step 2: Commit**

```bash
git add performance-tester-dotnet/src/PerformanceTester.Orchestration/Phases/TeardownPhaseDependencies.cs \
      performance-tester-dotnet/src/PerformanceTester.Orchestration/TestOrchestratorDependencies.cs
git commit -m "refactor: defer CT binding in TeardownPhaseDependencies to composition root"
```
