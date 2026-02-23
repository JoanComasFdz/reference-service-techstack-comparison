# Coding Guidelines Amendments Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add Guideline 36 (Option<T> for domain absence) and amend Guideline 34 (thin shell file organization with FP module naming).

**Architecture:** Documentation-only changes across 4 files. No code changes. Guideline 36 is appended (non-contiguous numbering, matching existing doc 02 pattern) to avoid renumbering cascade. Guideline 34 is amended in-place.

**Tech Stack:** Markdown documentation

---

## Task 1: Add Guideline 36 to Error Handling Document

**Files:**
- Modify: `coding-guidelines/03-error-handling.md`

**Step 1: Update document title and header**

Change line 1 from:
```markdown
# Error Handling with Result Types
```
to:
```markdown
# Error Handling and Absence
```

Change line 3 from:
```markdown
> Guidelines 15-17. For the full index and routing table, see [CODING_GUIDELINES.md](../CODING_GUIDELINES.md).
```
to:
```markdown
> Guidelines 15-17, 36. For the full index and routing table, see [CODING_GUIDELINES.md](../CODING_GUIDELINES.md).
```

Also update line 5 from:
```markdown
This codebase uses the `PerformanceTester.Functional` library (backed by [dunet](https://github.com/domn1995/dunet) discriminated unions) for typed error handling. These guidelines govern how Results are produced and consumed.
```
to:
```markdown
This codebase uses the `PerformanceTester.Functional` library (backed by [dunet](https://github.com/domn1995/dunet) discriminated unions) for typed error handling and domain absence. These guidelines govern how Results and Options are produced and consumed.
```

**Step 2: Append Guideline 36 after line 180**

Append the following at the end of the file (after the existing Guideline 17 content):

```markdown

---

### 36. Use `Option<T>` for Domain Absence, `T?` for Framework Interop

When a method legitimately produces "no value" — not a failure, just absence — use `Option<T>` from `PerformanceTester.Functional`. This forces callers to handle both cases via `Match`, preventing forgotten null checks. Use nullable `T?` only at framework/interop boundaries where .NET APIs return null.

**Decision table — which return type to use:**

| Situation | Return type | Example |
|-----------|-------------|---------|
| Operation failed with error details | `Result<T, TError>` (Guideline 15) | `ClearDatabase()` → `Result<Unit, ClearDatabaseError>` |
| Value may be absent (domain logic) | `Option<T>` | `TryConvertToMetrics()` → `Option<DockerMetrics>` |
| .NET API returns null | `T?` | `JsonSerializer.Deserialize<T>()` returns `T?` |

```csharp
// ✅ Good — domain absence expressed in return type
using static PerformanceTester.Functional.Option<DockerMetrics>;

public static Option<DockerMetrics> TryConvertToMetrics(
    ContainerStatsResponse stats,
    NonEmptyString containerName,
    DateTime timestamp)
{
    if (!HasValidPreCpuStats(stats))
    {
        return new None();
    }

    return new Some(new DockerMetrics
    {
        Timestamp = timestamp,
        ContainerId = ContainerId.FromString(stats.ID),
        ContainerName = containerName,
        CpuPercent = CpuPercent.FromDouble(CalculateCpuPercent(stats)),
        MemoryMB = MemoryMB.FromDouble(Math.Round(stats.MemoryStats.Usage / 1024.0 / 1024.0, 2))
    });
}

// Caller — forced to handle both cases
StatsProcessing.TryConvertToMetrics(stats, name, now).Match(
    some: s => ctx.CollectedMetrics.Add(s.Value),
    none: _ => { });

// ❌ Avoid — null for domain absence
public static DockerMetrics? TryConvertToMetrics(...)
{
    if (!HasValidPreCpuStats(stats))
    {
        return null;  // caller may forget to check
    }

    return new DockerMetrics { ... };
}
```

**`using static` for Option construction** follows the same pattern as Guideline 16 (Result):

```csharp
using static PerformanceTester.Functional.Option<PerformanceTester.DockerMonitoring.DockerMetrics>;
```

**Consuming `Option<T>`:** Use dunet `Match` at consumption points (branching on outcome, extracting values). Use `IsNone` / `IsSome` + early return (Guideline 11) in sequential pipelines, same split as `Result<T>` (Guideline 17).

```csharp
// ✅ Match — at consumption points
StatsProcessing.TryConvertToMetrics(stats, name, now).Match(
    some: s => ctx.CollectedMetrics.Add(s.Value),
    none: _ => { });

// ✅ IsNone + early return — in sequential pipelines (Guideline 11)
var metricOption = StatsProcessing.TryConvertToMetrics(stats, name, now);
if (metricOption.IsNone)
{
    return;
}

var metric = metricOption.SomeValue;
```

**Use `Match`** when both branches produce a value or side-effect.
**Use `IsNone` / `IsSome` + early return** when you need to short-circuit the enclosing method.

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

**When to use `Option<T>`:**

- The absence is a valid business state (no sample this interval, first Docker stats push has zeroed values)
- The caller must explicitly handle both cases
- The return type should communicate "this might not produce a value"

**When to use `T?`:**

- Framework APIs return null (`JsonSerializer`, `Process.GetProcessById`, file reads)
- The method is a thin wrapper around a nullable .NET API
- Private helpers where the calling code is within the same class and the null-coalescing pattern (`?? fallback`) is clear
```

**Step 3: Commit**

```bash
git add coding-guidelines/03-error-handling.md
git commit -m "docs: add Guideline 36 — Option<T> for domain absence"
```

---

## Task 2: Amend Guideline 34 File Organization

**Files:**
- Modify: `coding-guidelines/05-state-and-composition-patterns.md`

**Step 1: Replace the file organization section in Guideline 34**

Replace lines 370-376 (the existing file organization table):

```markdown
**File organization:**

| File                          | Content                        |
| ----------------------------- | ------------------------------ |
| `MonitorContext.cs`           | Mutable state record           |
| `MonitoringOperations.cs`    | Static logic functions         |
| `DockerMonitorService.cs`    | Thin shell (lifecycle only)    |
```

with:

```markdown
**File organization and naming:**

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
```

**Step 2: Update the code example (lines 261-313)**

The existing code example shows the 3-file split pattern (`MonitorContext`, `MonitoringOperations`, `DockerMonitorService` as separate classes). Update the class names and comments to match the new naming convention. Replace the code block at lines 261-313:

```csharp
// 1. Module — delegates, context record, static operations (Guideline 30)
internal static class DockerMonitorModule
{
    // Delegates
    public delegate void ReportDockerMonitorProgressDelegate(DockerMonitorPhaseInfo phaseInfo);
    public delegate Task StartDockerMonitoringDelegate(
        ReportDockerMonitorProgressDelegate reportProgress,
        CancellationToken ct = default);
    public delegate IReadOnlyCollection<DockerMetrics> GetDockerMetricsDelegate();

    // Context record — all mutable state, no logic (Guideline 32)
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

Keep the existing "avoid" example (lines 315-334) unchanged — it still shows the anti-pattern correctly.

**Step 3: Commit**

```bash
git add coding-guidelines/05-state-and-composition-patterns.md
git commit -m "docs: amend Guideline 34 — module + shell naming, co-locate with Guideline 30"
```

---

## Task 3: Update Routing Table and Summary Checklist

**Files:**
- Modify: `CODING_GUIDELINES.md`

**Step 1: Update guideline count**

Change line 5 from:
```markdown
The 35 guidelines are organized into 6 focused documents. Load only the document relevant to your current task.
```
to:
```markdown
The 36 guidelines are organized into 6 focused documents. Load only the document relevant to your current task.
```

**Step 2: Update Quick Reference routing table**

Change line 15 from:
```markdown
| Handling errors or using Result types | [Error Handling](coding-guidelines/03-error-handling.md) (Guidelines 15-17) |
```
to:
```markdown
| Handling errors, using Result types, or returning optional values | [Error Handling and Absence](coding-guidelines/03-error-handling.md) (Guidelines 15-17, 36) |
```

**Step 3: Update Document Overview for doc 03**

Change lines 30-31 from:
```markdown
### [03 - Error Handling](coding-guidelines/03-error-handling.md)
**Guidelines 15-17.** Result types instead of exceptions, `using static` for Result construction, dunet `Match` for exhaustive consumption, `IsFailure` + early return for sequential pipelines.
```
to:
```markdown
### [03 - Error Handling and Absence](coding-guidelines/03-error-handling.md)
**Guidelines 15-17, 36.** Result types instead of exceptions, `using static` for Result construction, dunet `Match` for exhaustive consumption, `IsFailure` + early return for sequential pipelines, `Option<T>` for domain absence vs `T?` for framework interop.
```

**Step 4: Add row 36 to Summary Checklist**

After line 84 (the last row, `| 35 | Delegate suffix | ...`), add:
```markdown
| 36 | Option for absence | Does this method return "no value" as a domain concept? Use `Option<T>`, not `T?` |
```

**Step 5: Commit**

```bash
git add CODING_GUIDELINES.md
git commit -m "docs: update routing table and checklist for Guideline 36"
```

---

## Task 4: Update CLAUDE.md Guideline Count

**Files:**
- Modify: `CLAUDE.md`

**Step 1: Update "35 guidelines" references**

There are 2 occurrences to change:

Line 559 — change from:
```markdown
**See also:** [CODING_GUIDELINES.md](CODING_GUIDELINES.md) for the coding guidelines index and routing table (35 guidelines across 6 focused documents in `coding-guidelines/`).
```
to:
```markdown
**See also:** [CODING_GUIDELINES.md](CODING_GUIDELINES.md) for the coding guidelines index and routing table (36 guidelines across 6 focused documents in `coding-guidelines/`).
```

Line 1002 — change from:
```markdown
- **[CODING_GUIDELINES.md](CODING_GUIDELINES.md)** - Coding guidelines index (35 guidelines across 6 documents in `coding-guidelines/`)
```
to:
```markdown
- **[CODING_GUIDELINES.md](CODING_GUIDELINES.md)** - Coding guidelines index (36 guidelines across 6 documents in `coding-guidelines/`)
```

**Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md guideline count to 36"
```

---

## Task 5: Verify All Cross-References

**Step 1: Search for any remaining "35 guidelines" references**

Run: `grep -r "35 guidelines" --include="*.md" .`
Expected: No matches (all updated in Tasks 3-4)

**Step 2: Search for any "Guidelines 15-17" references outside doc 03**

Run: `grep -r "Guidelines 15-17[^,]" --include="*.md" .`
Expected: No matches (all should now say "15-17, 36" or be in doc 03 itself)

**Step 3: Verify doc 03 header is consistent**

Run: `head -5 coding-guidelines/03-error-handling.md`
Expected: Title says "Error Handling and Absence", header says "Guidelines 15-17, 36"

**Step 4: Commit (only if fixes were needed)**

```bash
git add -A
git commit -m "docs: fix remaining cross-references for Guideline 36"
```
