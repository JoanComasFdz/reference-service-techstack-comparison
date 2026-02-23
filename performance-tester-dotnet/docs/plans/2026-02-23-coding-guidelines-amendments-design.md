# Coding Guidelines Amendments Design

**Date:** 2026-02-23
**Goal:** Address two gaps identified during the ProcessMonitoring refactoring plan review:
1. No guideline for `Option<T>` vs nullable reference types
2. Ambiguous interaction between Guideline 30 (FP module) and Guideline 34 (thin shell)

---

## Amendment 1: New Guideline 18 — Option<T> for Domain Absence

### Placement

Insert as **Guideline 18** in `coding-guidelines/03-error-handling.md`.

- Rename document title: "Error Handling with Result Types" → "Error Handling and Absence"
- Update header: "Guidelines 15-17" → "Guidelines 15-18"
- Renumber existing Guidelines 18-22 (Value Objects) → 19-23
- Update `04-value-objects.md` header: "Guidelines 18-22" → "Guidelines 19-23"
- Update routing table in `CODING_GUIDELINES.md` and all cross-references in other docs

### Guideline Content

**Title:** Use `Option<T>` for Domain Absence, `T?` for Framework Interop

**Core rule:** When a method legitimately produces "no value" — not a failure, just absence — use `Option<T>` from `PerformanceTester.Functional`. This forces callers to handle both cases via `Match`, preventing forgotten null checks. Use nullable `T?` only at framework/interop boundaries where .NET APIs return null.

**Decision table:**

| Situation | Return type | Example |
|-----------|-------------|---------|
| Operation failed with error details | `Result<T, TError>` (Guideline 15) | `ClearDatabase()` → `Result<Unit, ClearDatabaseError>` |
| Value may be absent (domain logic) | `Option<T>` | `TryConvertToMetrics()` → `Option<DockerMetrics>` |
| .NET API returns null | `T?` | `JsonSerializer.Deserialize<T>()` returns `T?` |

**Good — domain absence expressed in return type:**

```csharp
using static PerformanceTester.Functional.Option<DockerMetrics>;

public static Option<DockerMetrics> TryConvertToMetrics(
    ContainerStatsResponse stats, NonEmptyString containerName, DateTime timestamp)
{
    if (!HasValidPreCpuStats(stats))
    {
        return new None();
    }

    return new Some(new DockerMetrics { ... });
}
```

**Avoid — null for domain absence:**

```csharp
public static DockerMetrics? TryConvertToMetrics(...)
{
    if (!HasValidPreCpuStats(stats))
    {
        return null;  // caller may forget to check
    }
    return new DockerMetrics { ... };
}
```

**Acceptable — nullable at framework boundary:**

```csharp
// Framework returns null — wrapping in Option adds noise without safety
var report = JsonSerializer.Deserialize<ThroughputReport>(json);
if (report is null)
{
    logger.LogWarning("Failed to deserialize report");
    return null;  // fine — this IS the framework boundary
}
```

**`using static` for Option construction** follows the same pattern as Guideline 16 (Result):

```csharp
using static PerformanceTester.Functional.Option<PerformanceTester.DockerMonitoring.DockerMetrics>;
```

**Consuming `Option<T>`:** Use dunet `Match` at consumption points (branching on outcome, extracting values). Use `IsNone` / `IsSome` + early return (Guideline 11) in sequential pipelines, same split as `Result<T>` (Guideline 17).

```csharp
// Match — at consumption points
StatsProcessing.TryConvertToMetrics(stats, name, now).Match(
    some: s => ctx.CollectedMetrics.Add(s.Value),
    none: _ => { });

// IsNone + early return — in sequential pipelines (Guideline 11)
var metricOption = StatsProcessing.TryConvertToMetrics(stats, name, now);
if (metricOption.IsNone)
{
    return;
}
var metric = metricOption.SomeValue;
```

**Use `Match`** when both branches produce a value or side-effect.
**Use `IsNone` / `IsSome` + early return** when you need to short-circuit the enclosing method.

**When to use `Option<T>`:**

- The absence is a valid business state (no sample this interval, first Docker stats push has zeroed values)
- The caller must explicitly handle both cases
- The return type should communicate "this might not produce a value"

**When to use `T?`:**

- Framework APIs return null (`JsonSerializer`, `Process.GetProcessById`, file reads)
- The method is a thin wrapper around a nullable .NET API
- Private helpers where the calling code is within the same class and the null-coalescing pattern (`?? fallback`) is clear

---

## Amendment 2: Amend Guideline 34 — File Organization and Naming

### What Changes

Amend the existing **Guideline 34** in `coding-guidelines/05-state-and-composition-patterns.md`:

1. Replace the 3-row file organization table with a 2-row table
2. Add naming convention: `{Concept}Module` / `{Concept}BackgroundService`
3. Add paragraph connecting Guideline 34 back to Guideline 30 (FP module co-location)
4. Add accessibility rule for public vs internal types
5. Add "avoid" example showing the 6-file anti-pattern
6. Add ~500 line threshold for when to split

No renumbering needed — this is an in-place amendment.

### Amended File Organization Section

Replace the existing file organization table and add the following content:

**Naming:**

| File | Class | Content |
|------|-------|---------|
| `{Concept}Module.cs` | `internal static class {Concept}Module` | Delegates, context record, phase info, enums, static operations |
| `{Concept}BackgroundService.cs` | `internal sealed class {Concept}BackgroundService : BackgroundService` | Lifecycle wiring only |

**Combining with Guideline 30 (static class as module):** The shell gets its own file because it inherits from a framework base class and cannot be static. Everything else follows Guideline 30 — co-locate delegate definitions, records (context, phase info, output), enums, and static functions in a single module file. No subdirectory needed.

**Accessibility rule:** Internal types (delegates, context record, phase info, operations) belong inside the module. Public data contracts consumed by other slices (e.g., `DockerMetrics`, `ProcessMetrics`) stay in separate files at the project root — they cannot be nested inside an `internal static class` and remain accessible to other projects.

```
// ✅ Good — module file + shell file, no subdirectory
ProcessMonitoring/
├── ProcessMonitorModule.cs              ← internal: delegates, context, phase info, operations
├── ProcessMonitorBackgroundService.cs   ← internal: BackgroundService shell
├── ProcessMetrics.cs                    ← public: data record consumed by other slices
├── ProcessCpuCalculator.cs              ← internal: standalone utility (unchanged)
├── ProcessNameExtractor.cs              ← public: standalone utility (unchanged)
├── ServiceCollectionExtensions.cs       ← public: DI registration
└── ValueObjects/SampleCount.cs          ← public: value object

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

**Why:** Delegates, records, and enums that form a module's contract belong together — they change for the same reasons (Guideline 7) and are consumed together. Splitting them into individual files forces file-hopping to understand the module. The module file reads top-to-bottom: delegates → records → static operations (same reading order as Guideline 30).

**When the module file grows too large:** If the co-located module exceeds ~500 lines, extract the context record as the first split point. The reading order (delegates → records → operations) stays intact in the module file.

---

## Ripple Effects Summary

### Files Modified

| File | Change |
|------|--------|
| `coding-guidelines/03-error-handling.md` | Add Guideline 18, rename title, update header |
| `coding-guidelines/04-value-objects.md` | Renumber 18-22 → 19-23, update header |
| `coding-guidelines/05-state-and-composition-patterns.md` | Amend Guideline 34 file organization section |
| `CODING_GUIDELINES.md` | Update routing table, renumber summary checklist |

### Cross-References to Update

All guideline documents that reference Guidelines 18-22 by number must be updated to 19-23. Search for:
- "Guideline 18" / "Guideline 19" / "Guideline 20" / "Guideline 21" / "Guideline 22"
- "#18" / "#19" / "#20" / "#21" / "#22" in markdown links

### No Code Changes

These are documentation-only amendments. Code changes (applying the new guidelines to existing code) will be handled by separate plans.
