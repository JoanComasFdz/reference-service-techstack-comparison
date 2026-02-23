# File Structure Convention Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Codify the `Api.cs` + `Internal/` file structure convention into Guideline 05-06, then update the ProcessMonitoring refactoring plan to use the new convention.

**Architecture:** Update Guideline 05-06's file organization section to replace the flat-at-root approach with a visibility-first structure (`Api.cs` at root for public contract, `Internal/` directory for implementation). Then systematically update the ProcessMonitoring refactoring plan so that when it is later executed, it produces the correct file structure.

**Tech Stack:** Markdown documentation only (no code changes in this plan)

---

## Task 1: Update Guideline 05-06 — File Organization Section

**Files:**
- Modify: `coding-guidelines/05-state-and-composition-patterns.md` (lines 378-451)

This replaces the current "File organization and naming" section of Guideline 05-06 with the new visibility-first convention from the approved design (`docs/plans/2026-02-23-file-structure-convention-design.md`).

**Step 1: Read the current guideline section**

Read `coding-guidelines/05-state-and-composition-patterns.md` lines 378-451 to confirm the exact content to replace.

**Step 2: Replace the file organization section**

Replace everything from line 378 (`**File organization and naming:**`) through line 451 (end of file) with the following content:

````markdown
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

```
// ✅ Good — visibility-first structure
ProcessMonitoring/
├── Api.cs                               ← public: delegates, phase info, ProcessMetrics, ProcessNameExtractor
├── ServiceCollectionExtensions.cs       ← public: DI registration
├── ValueObjects/SampleCount.cs          ← public: value object (has validation logic)
└── Internal/
    ├── ProcessMonitorModule.cs          ← internal: context record, static operations, ProcessCpuCalculator
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
    internal sealed class ProcessCpuCalculator { ... }    // nested — only used by operations
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
````

**Step 3: Verify no broken cross-references**

Search the codebase for references to the old "no subdirectory" phrasing. Common locations:
- `CLAUDE.md` — should not reference 05-06 file organization details
- Other plan documents — check `docs/plans/` for references to old structure
- Source code HTML comments — search for `guideline #05-06` in `.cs` files

Run: `grep -r "no subdirectory" coding-guidelines/ docs/ --include="*.md" -l`

**Step 4: Commit**

```bash
git add coding-guidelines/05-state-and-composition-patterns.md
git commit -m "docs(guidelines): update 05-06 with Api.cs + Internal/ file structure convention"
```

---

## Task 2: Update Refactoring Plan — Header, Target Structure, and File Lists

**Files:**
- Modify: `docs/plans/2026-02-22-process-monitoring-refactoring.review.md` (lines 5-63)

Update the architecture description, target structure diagram, files deleted list, and consumer/test update sections to reflect the new `Api.cs` + `Internal/` convention.

**Step 1: Update the architecture description (line 7)**

Replace line 7's architecture description with:

```markdown
**Architecture:** Remove `IProcessMonitor` interface. Consolidate all public types (delegates, phase info, data records, utilities) into `Api.cs` at project root. Extract mutable state into `MonitorContext` record (Guideline 05-04). Extract business logic into static operations inside `Internal/ProcessMonitorModule.cs`. Create thin shell `Internal/ProcessMonitorBackgroundService.cs` that only owns lifecycle (Guideline 05-06). Expose public API via named delegates registered in DI (Guidelines 02-01 to 02-03). Use value objects for `ProcessMonitorPhaseInfo` fields (Guideline 04-01).
```

**Step 2: Replace the target structure (lines 27-46)**

Replace the HTML comment on line 27, the target structure block (lines 28-39), and the files deleted block (lines 41-46) with:

````markdown
<!-- applied guideline #05-06: visibility-first file structure — Api.cs at root contains all public types (delegates, phase info, ProcessMetrics, ProcessNameExtractor); Internal/ contains module (context, operations, CPU calculator) and shell; ValueObjects/ stays at root; no flat mixing of public and internal files -->
### Target Structure (Functional, delegate-based)
```
ProcessMonitoring/
├── Api.cs                               ← NEW: all public types (delegates, phase info, ProcessMetrics, ProcessNameExtractor)
├── ServiceCollectionExtensions.cs       ← REWRITE: Register delegates instead of interface
├── ValueObjects/
│   └── SampleCount.cs                   ← NEW: NonNegativeInt value object
└── Internal/
    ├── ProcessMonitorModule.cs          ← NEW: context record, static operations, ProcessCpuCalculator (all internal)
    └── ProcessMonitorBackgroundService.cs ← NEW: thin shell (BackgroundService lifecycle only)
```

### Files Deleted
```
├── IProcessMonitor.cs                    ← DELETED: Replaced by named delegates (in Api.cs)
├── ProcessMonitorPhaseInfo.cs (root)     ← DELETED: Content merged into Api.cs
├── ProcessMonitorService.cs (root)       ← DELETED: Replaced by Internal/ProcessMonitorBackgroundService.cs
├── ProcessMetrics.cs (root)              ← DELETED: Content merged into Api.cs
├── ProcessNameExtractor.cs (root)        ← DELETED: Content merged into Api.cs
├── ProcessCpuCalculator.cs (root)        ← DELETED: Content nested in Internal/ProcessMonitorModule.cs
```
````

**Step 3: Verify consumer/test update sections (lines 48-63)**

These sections list the files that need updating in Orchestration and test projects. They remain correct — the file paths in those sections are for consumer code, not ProcessMonitoring itself. No changes needed, but verify by re-reading lines 48-63.

**Step 4: Commit**

```bash
git add docs/plans/2026-02-22-process-monitoring-refactoring.review.md
git commit -m "docs(plan): update ProcessMonitoring target structure for Api.cs + Internal/ convention"
```

---

## Task 3: Update Refactoring Plan — Task 2 (Api.cs + Internal/ProcessMonitorModule.cs)

**Files:**
- Modify: `docs/plans/2026-02-22-process-monitoring-refactoring.review.md` (lines 127-537)

This is the biggest change. The current Task 2 creates a single `ProcessMonitorModule.cs` at root with everything nested inside. The new Task 2 creates two files: `Api.cs` (public types) and `Internal/ProcessMonitorModule.cs` (internal types).

**Step 1: Replace the Task 2 header and description (lines 127-139)**

Replace with:

````markdown
<!-- applied guideline #05-06: visibility-first file structure — public types (delegates, phase info, ProcessMetrics, ProcessNameExtractor) go to Api.cs at project root; internal types (context record, operations, CPU calculator) go to Internal/ProcessMonitorModule.cs -->
## Task 2: Create Api.cs and Internal/ProcessMonitorModule.cs

**Files:**
- Create: `src/PerformanceTester.ProcessMonitoring/Api.cs`
- Create: `src/PerformanceTester.ProcessMonitoring/Internal/ProcessMonitorModule.cs`

Two files that together replace `IProcessMonitor.cs`, `ProcessMonitorPhaseInfo.cs`, `ProcessMonitorService.cs`, `ProcessMetrics.cs`, `ProcessNameExtractor.cs`, and `ProcessCpuCalculator.cs`. Public types go to `Api.cs` (Guideline 05-06 visibility-first structure). Internal types go to the module file in `Internal/` (Guideline 02-05 reading order: context record → static operations).
````

**Step 2: Replace the code block — split into Api.cs and Internal/ProcessMonitorModule.cs**

Replace the entire code block (lines 141-523) with two code blocks:

**Api.cs code block:**

The first code block contains all public types extracted from what was previously nested inside `ProcessMonitorModule`. These are: delegates, phase info enums/record, ProcessMetrics record, and ProcessNameExtractor. The reading order follows Guideline 02-05: delegates → records/enums → data contracts → utilities.

Provide the complete `Api.cs` content:

```csharp
using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.ProcessMonitoring.ValueObjects;

namespace PerformanceTester.ProcessMonitoring;

// =====================================================================
// Delegates (Guideline 02-05 reading order: delegates first)
// =====================================================================

/// <summary>
/// Reports process monitoring phase changes.
/// Replaces IProgress&lt;ProcessMonitorPhaseInfo&gt; with a named delegate per Guideline 02-02.
/// </summary>
public delegate void ReportProcessMonitorProgressDelegate(ProcessMonitorPhaseInfo phaseInfo);

/// <summary>
/// Starts process resource monitoring for the given PID.
/// Blocks until the first sample has been collected.
/// </summary>
public delegate Task StartProcessMonitoringDelegate(
    ProcessId processId,
    ReportProcessMonitorProgressDelegate reportProgress,
    CancellationToken ct = default);

/// <summary>
/// Returns all collected process metrics in chronological order.
/// Call after test completion (after stopping IHost).
/// </summary>
public delegate IReadOnlyCollection<ProcessMetrics> GetProcessMetricsDelegate();

// =====================================================================
// Phase info — enums and record struct
// =====================================================================

/// <summary>
/// Represents the phases in process monitoring lifecycle.
/// </summary>
public enum ProcessMonitorPhase
{
    /// <summary>Monitoring has been requested via StartMonitoringAsync().</summary>
    MonitoringRequested,

    /// <summary>First metrics sample has been collected.</summary>
    FirstSampleCollected,

    /// <summary>A sample has been collected (reported after each sample).</summary>
    SampleCollected,

    /// <summary>Process was not found during initial lookup.</summary>
    ProcessNotFound,

    /// <summary>Process has exited during monitoring.</summary>
    ProcessExited,

    /// <summary>Monitoring has stopped.</summary>
    MonitoringStopped
}

/// <summary>
/// Represents the state of a phase transition.
/// Aligns with DockerMonitorPhaseState and ConsumerPhaseState from other slices.
/// </summary>
public enum ProcessMonitorPhaseState
{
    /// <summary>Phase is about to start.</summary>
    Starting,

    /// <summary>Phase has completed successfully.</summary>
    Completed,

    /// <summary>Phase failed with an error.</summary>
    Failed
}

/// <summary>
/// Information about a phase transition in process monitoring.
/// Aligns with DockerMonitorPhaseInfo pattern from DockerMonitoring slice.
/// </summary>
/// <param name="Phase">The phase that is transitioning.</param>
/// <param name="State">The state of the transition (Starting, Completed, Failed).</param>
/// <param name="ProcessId">ID of the process being monitored.</param>
/// <param name="SampleCount">Current total sample count.</param>
/// <param name="Message">Optional descriptive message.</param>
/// <param name="Timestamp">When the phase occurred.</param>
public readonly record struct ProcessMonitorPhaseInfo(
    ProcessMonitorPhase Phase,
    ProcessMonitorPhaseState State,
    ProcessId ProcessId,
    SampleCount SampleCount,
    string? Message = null,
    DateTimeOffset? Timestamp = null)
{
    /// <summary>Gets the timestamp, defaulting to now if not specified.</summary>
    public DateTimeOffset TimestampOrNow => Timestamp ?? DateTimeOffset.UtcNow;

    /// <summary>Creates a ProcessMonitorPhaseInfo indicating a phase is starting.</summary>
    <!-- applied guideline #06-04: expression body must start on same line as => -->
    public static ProcessMonitorPhaseInfo Starting(
        ProcessMonitorPhase phase,
        ProcessId processId,
        string? message = null) => new(
        phase,
        ProcessMonitorPhaseState.Starting,
        processId,
        SampleCount.FromInt(0),
        message,
        DateTimeOffset.UtcNow);

    /// <summary>Creates a ProcessMonitorPhaseInfo indicating a phase has completed.</summary>
    public static ProcessMonitorPhaseInfo Completed(
        ProcessMonitorPhase phase,
        ProcessId processId,
        string? message = null) => new(
        phase,
        ProcessMonitorPhaseState.Completed,
        processId,
        SampleCount.FromInt(0),
        message,
        DateTimeOffset.UtcNow);

    /// <summary>Creates a ProcessMonitorPhaseInfo indicating a phase has completed with sample count.</summary>
    public static ProcessMonitorPhaseInfo Completed(
        ProcessMonitorPhase phase,
        ProcessId processId,
        SampleCount sampleCount,
        string? message = null) => new(
        phase,
        ProcessMonitorPhaseState.Completed,
        processId,
        sampleCount,
        message,
        DateTimeOffset.UtcNow);

    /// <summary>Creates a ProcessMonitorPhaseInfo indicating a phase has failed.</summary>
    public static ProcessMonitorPhaseInfo Failed(
        ProcessMonitorPhase phase,
        ProcessId processId,
        string? message = null) => new(
        phase,
        ProcessMonitorPhaseState.Failed,
        processId,
        SampleCount.FromInt(0),
        message,
        DateTimeOffset.UtcNow);
}

// =====================================================================
// Data records — public output contracts
// =====================================================================

/// <summary>
/// Snapshot of process resource metrics at a point in time.
/// </summary>
public record ProcessMetrics(
    DateTimeOffset Timestamp,
    int ProcessId,
    string ProcessName,
    double CpuPercent,
    double MemoryMB,
    int ThreadCount);

// =====================================================================
// Public utilities
// =====================================================================

/// <summary>
/// Extracts meaningful process names from command-line arguments.
/// </summary>
public static class ProcessNameExtractor
{
    /// <summary>
    /// Extracts a meaningful service name from a process name and its command line arguments.
    /// </summary>
    public static string ExtractMeaningfulName(string processName, string[]? commandLine)
    {
        if (commandLine is null || commandLine.Length == 0)
        {
            return processName;
        }

        // For "dotnet" processes, find the DLL name
        if (processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var dllArg = commandLine.FirstOrDefault(arg =>
                arg.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

            if (dllArg is not null)
            {
                return Path.GetFileNameWithoutExtension(dllArg);
            }
        }

        // For Java processes, find the main class or JAR name
        if (processName.Equals("java", StringComparison.OrdinalIgnoreCase))
        {
            var jarArg = commandLine
                .SkipWhile(arg => arg != "-jar")
                .Skip(1)
                .FirstOrDefault();

            if (jarArg is not null)
            {
                return Path.GetFileNameWithoutExtension(jarArg);
            }

            // Look for main class (last argument that looks like a class name)
            var mainClass = commandLine.LastOrDefault(arg =>
                !arg.StartsWith('-') && arg.Contains('.') && !arg.EndsWith(".jar"));

            if (mainClass is not null)
            {
                return mainClass.Split('.').Last();
            }
        }

        return processName;
    }
}
```

**Internal/ProcessMonitorModule.cs code block:**

The second code block contains only internal types: the context record, the CPU calculator (nested), and all static operations. No public delegates or phase info.

Provide the complete `Internal/ProcessMonitorModule.cs` content:

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.ProcessMonitoring.ValueObjects;

namespace PerformanceTester.ProcessMonitoring.Internal;

/// <summary>
/// Internal module (Guideline 05-06 + 02-05): context record → static operations.
/// All internal implementation for process monitoring. Public types are in Api.cs.
/// </summary>
internal static class ProcessMonitorModule
{
    // =====================================================================
    // Context record — all mutable state, no logic (Guideline 05-04)
    // =====================================================================

    /// <summary>
    /// Centralizes all mutable state for process monitoring (Guideline 05-04).
    /// Passed explicitly to static operations — no hidden fields.
    /// </summary>
    internal sealed record MonitorContext
    {
        /// <summary>Thread-safe collection of all sampled metrics.</summary>
        public ConcurrentBag<ProcessMetrics> CollectedMetrics { get; } = new();

        /// <summary>Signal from StartMonitoringAsync → ExecuteAsync (deferred start).</summary>
        public TaskCompletionSource StartSignal { get; } = new();

        /// <summary>Signal from sampling loop → StartMonitoringAsync (first sample collected).</summary>
        public TaskCompletionSource FirstSampleCollected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Cached process name extracted from command line (set once, read many).</summary>
        public string? CachedProcessName { get; set; }
    }

    // =====================================================================
    // Nested utility — only used by operations below
    // =====================================================================

    /// <summary>
    /// Calculates CPU usage percentage from process time deltas between samples.
    /// </summary>
    internal sealed class ProcessCpuCalculator
    {
        private TimeSpan _previousTotalProcessorTime;
        private DateTime _previousSampleTime;
        private bool _initialized;

        /// <summary>
        /// Samples the current CPU time of the process and returns CPU usage percentage since last sample.
        /// First call always returns 0.0 (establishing baseline).
        /// </summary>
        public double Sample(Process process)
        {
            var currentTotalProcessorTime = process.TotalProcessorTime;
            var currentSampleTime = DateTime.UtcNow;

            if (!_initialized)
            {
                _previousTotalProcessorTime = currentTotalProcessorTime;
                _previousSampleTime = currentSampleTime;
                _initialized = true;
                return 0.0;
            }

            var cpuUsedMs = (currentTotalProcessorTime - _previousTotalProcessorTime).TotalMilliseconds;
            var elapsedMs = (currentSampleTime - _previousSampleTime).TotalMilliseconds;

            _previousTotalProcessorTime = currentTotalProcessorTime;
            _previousSampleTime = currentSampleTime;

            if (elapsedMs <= 0)
            {
                return 0.0;
            }

            return cpuUsedMs / elapsedMs * 100.0;
        }
    }

    // =====================================================================
    // Static operations
    // =====================================================================

    /// <summary>
    /// Runs the sampling loop: initializes the process, then samples at regular intervals
    /// until cancellation or process exit.
    /// </summary>
    public static async Task RunSamplingLoopAsync(
        MonitorContext ctx,
        ProcessId processId,
        TimeSpan samplingInterval,
        ReportProcessMonitorProgressDelegate reportProgress,
        ILogger logger,
        CancellationToken ct)
    {
        Process? process = null;

        try
        {
            process = InitializeProcess(ctx, processId, logger);
            if (process is null)
            {
                ctx.FirstSampleCollected.TrySetResult();

                reportProgress(ProcessMonitorPhaseInfo.Completed(
                    ProcessMonitorPhase.ProcessNotFound,
                    processId,
                    message: $"Process {processId} not found"));
                return;
            }

            // Initialize CPU calculator (first sample returns 0.0)
            var cpuCalculator = new ProcessCpuCalculator();
            cpuCalculator.Sample(process);

            using var timer = new PeriodicTimer(samplingInterval);

            while (await timer.WaitForNextTickAsync(ct))
            {
                if (!CollectSample(ctx, process, cpuCalculator, processId, reportProgress, logger))
                {
                    break;
                }
            }

            logger.LogInformation("ProcessMonitor stopping gracefully");
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("ProcessMonitor cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ProcessMonitor failed");
            ctx.FirstSampleCollected.TrySetException(ex);
            throw;
        }
        finally
        {
            var count = SampleCount.FromInt(ctx.CollectedMetrics.Count);
            logger.LogInformation("ProcessMonitor completed: {Count} samples collected", count);

            reportProgress(ProcessMonitorPhaseInfo.Completed(
                ProcessMonitorPhase.MonitoringStopped,
                processId,
                count,
                message: $"Monitoring stopped for PID {processId}, collected {count} samples"));

            process?.Dispose();
        }
    }

    /// <summary>
    /// Looks up the process by ID, reads its command line, and caches the meaningful name.
    /// Returns null if the process was not found.
    /// </summary>
    private static Process? InitializeProcess(
        MonitorContext ctx,
        ProcessId processId,
        ILogger logger)
    {
        try
        {
            var process = Process.GetProcessById(processId.Value);

            var commandLine = ReadCommandLine(processId.Value);
            ctx.CachedProcessName = ProcessNameExtractor.ExtractMeaningfulName(
                process.ProcessName,
                commandLine);

            logger.LogInformation(
                "Monitoring process: {ProcessName} (PID: {ProcessId})",
                ctx.CachedProcessName,
                processId);

            return process;
        }
        catch (ArgumentException ex)
        {
            logger.LogError(ex, "Process {ProcessId} not found", processId);
            return null;
        }
    }

    /// <summary>
    /// Collects a single sample: checks process status, captures metrics, stores them.
    /// Returns true to continue sampling, false to break.
    /// </summary>
    private static bool CollectSample(
        MonitorContext ctx,
        Process process,
        ProcessCpuCalculator cpuCalculator,
        ProcessId processId,
        ReportProcessMonitorProgressDelegate reportProgress,
        ILogger logger)
    {
        try
        {
            if (process.HasExited)
            {
                logger.LogWarning("Process {ProcessId} has exited", processId);

                var exitedCount = SampleCount.FromInt(ctx.CollectedMetrics.Count);
                reportProgress(ProcessMonitorPhaseInfo.Completed(
                    ProcessMonitorPhase.ProcessExited,
                    processId,
                    exitedCount,
                    message: $"Process {processId} has exited"));
                return false;
            }

            process.Refresh();

            var cpuPercent = cpuCalculator.Sample(process);
            var memoryMB = process.WorkingSet64 / 1024.0 / 1024.0;
            var threadCount = process.Threads.Count;

            var metrics = new ProcessMetrics(
                Timestamp: DateTimeOffset.UtcNow,
                ProcessId: processId.Value,
                ProcessName: ctx.CachedProcessName ?? process.ProcessName,
                CpuPercent: Math.Round(cpuPercent, 2),
                MemoryMB: Math.Round(memoryMB, 2),
                ThreadCount: threadCount);

            ctx.CollectedMetrics.Add(metrics);
            var currentCount = SampleCount.FromInt(ctx.CollectedMetrics.Count);

            var isFirstSample = ctx.FirstSampleCollected.TrySetResult();
            if (isFirstSample)
            {
                reportProgress(ProcessMonitorPhaseInfo.Completed(
                    ProcessMonitorPhase.FirstSampleCollected,
                    processId,
                    currentCount,
                    message: $"First sample collected for PID {processId}"));
            }

            reportProgress(ProcessMonitorPhaseInfo.Completed(
                ProcessMonitorPhase.SampleCollected,
                processId,
                currentCount,
                message: $"Sample #{currentCount} collected"));

            return true;
        }
        catch (InvalidOperationException)
        {
            logger.LogWarning("Process {ProcessId} terminated during sampling", processId);

            var terminatedCount = SampleCount.FromInt(ctx.CollectedMetrics.Count);
            reportProgress(ProcessMonitorPhaseInfo.Completed(
                ProcessMonitorPhase.ProcessExited,
                processId,
                terminatedCount,
                message: $"Process {processId} terminated during sampling"));
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sampling process {ProcessId}", processId);
            return true;
        }
    }

    private static string[]? ReadCommandLine(int processId)
    {
        var cmdLinePath = $"/proc/{processId}/cmdline";
        if (!File.Exists(cmdLinePath))
        {
            return null;
        }

        try
        {
            var content = File.ReadAllText(cmdLinePath);
            return content.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
```

**Step 3: Update the build verification step**

Replace the current step about build verification (around line 526-529) with:

```markdown
**Step 3: Verify it builds**

Run: `dotnet build src/PerformanceTester.ProcessMonitoring`
Expected: Build succeeded, 0 warnings. The old `ProcessMonitorPhaseInfo.cs`, `ProcessMetrics.cs`, `ProcessNameExtractor.cs`, and `ProcessCpuCalculator.cs` at root will cause ambiguity/duplicate errors — delete them in Task 4.
```

**Step 4: Update the commit step**

Replace the commit step with:

```markdown
**Step 4: Commit**

` ``bash
git add src/PerformanceTester.ProcessMonitoring/Api.cs \
        src/PerformanceTester.ProcessMonitoring/Internal/ProcessMonitorModule.cs
git commit -m "feat(ProcessMonitoring): add Api.cs with public types and Internal/ProcessMonitorModule with operations"
` ``
```

(Note: the backtick spacing above is to avoid breaking the markdown fence. The actual plan should use proper triple backticks.)

**Step 5: Commit the plan update**

```bash
git add docs/plans/2026-02-22-process-monitoring-refactoring.review.md
git commit -m "docs(plan): rewrite Task 2 for Api.cs + Internal/ProcessMonitorModule.cs"
```

---

## Task 4: Update Refactoring Plan — Task 3 (Internal/ProcessMonitorBackgroundService.cs)

**Files:**
- Modify: `docs/plans/2026-02-22-process-monitoring-refactoring.review.md` (lines 540-674, the Task 3 section)

The shell file moves from project root to `Internal/` and uses the `.Internal` namespace.

**Step 1: Update the Task 3 header and file path**

Replace the Task 3 header (around line 540) and file reference:

```markdown
## Task 3: Create Thin Shell Internal/ProcessMonitorBackgroundService

**Files:**
- Create: `src/PerformanceTester.ProcessMonitoring/Internal/ProcessMonitorBackgroundService.cs`
```

Update the description to reference `Internal/` and the `.Internal` namespace.

**Step 2: Update the code block**

In the `ProcessMonitorBackgroundService.cs` code block, make these changes:

1. Change the namespace from `PerformanceTester.ProcessMonitoring` to `PerformanceTester.ProcessMonitoring.Internal`
2. Replace `using static PerformanceTester.ProcessMonitoring.ProcessMonitorModule;` with just `using PerformanceTester.ProcessMonitoring;` (for public types from Api.cs) — the module is in the same `.Internal` namespace so no import needed for it
3. Change `MonitorContext` references to `ProcessMonitorModule.MonitorContext` (since MonitorContext is nested inside the module)

The updated code block header:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.ProcessMonitoring.Internal;
```

The `using static` import is no longer needed because:
- Public types (delegates, phase info) are in `PerformanceTester.ProcessMonitoring` namespace (accessed via `using`)
- `ProcessMonitorModule` is in the same `Internal` namespace (no import needed)
- `MonitorContext` is accessed as `ProcessMonitorModule.MonitorContext`

**Step 3: Update the HTML comment**

Replace the guideline comment at the top of Task 3 to reference the new convention:

```markdown
<!-- applied guideline #05-06: shell file placed in Internal/ directory with .Internal namespace; references public types from Api.cs via using PerformanceTester.ProcessMonitoring -->
```

**Step 4: Update commit step**

```markdown
**Step 3: Commit**

` ``bash
git add src/PerformanceTester.ProcessMonitoring/Internal/ProcessMonitorBackgroundService.cs
git commit -m "feat(ProcessMonitoring): add thin shell Internal/ProcessMonitorBackgroundService"
` ``
```

**Step 5: Commit the plan update**

```bash
git add docs/plans/2026-02-22-process-monitoring-refactoring.review.md
git commit -m "docs(plan): update Task 3 for Internal/ path and namespace"
```

---

## Task 5: Update Refactoring Plan — Task 4 (ServiceCollectionExtensions + Deletions)

**Files:**
- Modify: `docs/plans/2026-02-22-process-monitoring-refactoring.review.md` (lines 677-776, the Task 4 section)

The delegate types are now standalone in `PerformanceTester.ProcessMonitoring` namespace (not nested in a module class). More files need deleting. The `ServiceCollectionExtensions.cs` references change accordingly.

**Step 1: Update the ServiceCollectionExtensions code block**

Key changes to the code:

1. Remove `using static PerformanceTester.ProcessMonitoring.ProcessMonitorModule;` — delegates are now top-level in the `PerformanceTester.ProcessMonitoring` namespace
2. Change `ProcessMonitorBackgroundService` references to `Internal.ProcessMonitorBackgroundService` (or add `using PerformanceTester.ProcessMonitoring.Internal;`)
3. Delegate type names are now unqualified: `StartProcessMonitoringDelegate` (not `ProcessMonitorModule.StartProcessMonitoringDelegate`)

Updated using block:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.ProcessMonitoring.Internal;

namespace PerformanceTester.ProcessMonitoring;
```

The rest of the `ServiceCollectionExtensions` code stays the same — it already references `StartProcessMonitoringDelegate` and `GetProcessMetricsDelegate` which are now directly in the namespace.

**Step 2: Update the files deleted list**

Replace the current "Delete these three files" with:

```markdown
**Step 2: Delete old files**

Delete these six files that are replaced by Api.cs, Internal/ProcessMonitorModule.cs, and Internal/ProcessMonitorBackgroundService.cs:
- `src/PerformanceTester.ProcessMonitoring/IProcessMonitor.cs`
- `src/PerformanceTester.ProcessMonitoring/ProcessMonitorService.cs`
- `src/PerformanceTester.ProcessMonitoring/ProcessMonitorPhaseInfo.cs`
- `src/PerformanceTester.ProcessMonitoring/ProcessMetrics.cs`
- `src/PerformanceTester.ProcessMonitoring/ProcessNameExtractor.cs`
- `src/PerformanceTester.ProcessMonitoring/ProcessCpuCalculator.cs`
```

**Step 3: Update the commit step**

```markdown
**Step 4: Commit**

` ``bash
git add src/PerformanceTester.ProcessMonitoring/ServiceCollectionExtensions.cs
git rm src/PerformanceTester.ProcessMonitoring/IProcessMonitor.cs
git rm src/PerformanceTester.ProcessMonitoring/ProcessMonitorService.cs
git rm src/PerformanceTester.ProcessMonitoring/ProcessMonitorPhaseInfo.cs
git rm src/PerformanceTester.ProcessMonitoring/ProcessMetrics.cs
git rm src/PerformanceTester.ProcessMonitoring/ProcessNameExtractor.cs
git rm src/PerformanceTester.ProcessMonitoring/ProcessCpuCalculator.cs
git commit -m "refactor(ProcessMonitoring): rewrite DI registration for standalone delegates, delete old files"
` ``
```

**Step 4: Commit the plan update**

```bash
git add docs/plans/2026-02-22-process-monitoring-refactoring.review.md
git commit -m "docs(plan): update Task 4 for standalone delegates and expanded file deletions"
```

---

## Task 6: Update Refactoring Plan — Task 5 (Orchestration Consumers)

**Files:**
- Modify: `docs/plans/2026-02-22-process-monitoring-refactoring.review.md` (lines 779-868, the Task 5 section)

The Orchestration consumers now reference standalone delegate types instead of module-nested ones.

**Step 1: Update EventTestPhase.cs references**

In the "After" code blocks, change all `ProcessMonitorModule.StartProcessMonitoringDelegate` to just `StartProcessMonitoringDelegate`:

Before (in plan):
```csharp
var startProcessMonitoring = services.GetRequiredService<ProcessMonitorModule.StartProcessMonitoringDelegate>();
```

After (updated plan):
```csharp
var startProcessMonitoring = services.GetRequiredService<StartProcessMonitoringDelegate>();
```

**Step 2: Update ReportingPhase.cs references**

Same pattern — change `ProcessMonitorModule.GetProcessMetricsDelegate` to `GetProcessMetricsDelegate`:

Before (in plan):
```csharp
var getProcessMetrics = services.GetRequiredService<ProcessMonitorModule.GetProcessMetricsDelegate>();
```

After (updated plan):
```csharp
var getProcessMetrics = services.GetRequiredService<GetProcessMetricsDelegate>();
```

**Step 3: Update the namespace note**

The HTML comments referencing namespace changes (lines ~804, 828-829) should be updated. The current comments say "namespace changed from `PerformanceTester.ProcessMonitoring.Monitoring` to `PerformanceTester.ProcessMonitoring`". Update to note that delegates are standalone types in `PerformanceTester.ProcessMonitoring` (from `Api.cs`).

**Step 4: Commit the plan update**

```bash
git add docs/plans/2026-02-22-process-monitoring-refactoring.review.md
git commit -m "docs(plan): update Task 5 for standalone delegate references in Orchestration"
```

---

## Task 7: Update Refactoring Plan — Task 6 (Integration Tests)

**Files:**
- Modify: `docs/plans/2026-02-22-process-monitoring-refactoring.review.md` (lines 872-976, the Task 6 section)

The test infrastructure references change from module-qualified delegate types to standalone types.

**Step 1: Update namespace import notes**

Replace all instructions to add `using static PerformanceTester.ProcessMonitoring.ProcessMonitorModule;` with instructions to add `using PerformanceTester.ProcessMonitoring;` (which covers all public types from `Api.cs`).

**Step 2: Update delegate type references in test code**

Throughout Task 6, change:
- `ProcessMonitorModule.StartProcessMonitoringDelegate` → `StartProcessMonitoringDelegate`
- `ProcessMonitorModule.GetProcessMetricsDelegate` → `GetProcessMetricsDelegate`
- `ProcessMonitorModule.ReportProcessMonitorProgressDelegate` → `ReportProcessMonitorProgressDelegate`

Specific "Before/After" code blocks to update:

In the `ProcessMonitoring.cs` facade (Step 3):
```csharp
// Old plan:
public ProcessMonitorModule.StartProcessMonitoringDelegate StartMonitoring { get; private set; } = null!;
public ProcessMonitorModule.GetProcessMetricsDelegate GetMetrics { get; private set; } = null!;
StartMonitoring = _host.Services.GetRequiredService<ProcessMonitorModule.StartProcessMonitoringDelegate>();
GetMetrics = _host.Services.GetRequiredService<ProcessMonitorModule.GetProcessMetricsDelegate>();

// Updated plan:
public StartProcessMonitoringDelegate StartMonitoring { get; private set; } = null!;
public GetProcessMetricsDelegate GetMetrics { get; private set; } = null!;
StartMonitoring = _host.Services.GetRequiredService<StartProcessMonitoringDelegate>();
GetMetrics = _host.Services.GetRequiredService<GetProcessMetricsDelegate>();
```

**Step 3: Commit the plan update**

```bash
git add docs/plans/2026-02-22-process-monitoring-refactoring.review.md
git commit -m "docs(plan): update Task 6 for standalone delegate references in tests"
```

---

## Task 8: Final Review and Squash Commits

**Step 1: Review the complete updated refactoring plan**

Read through the entire `docs/plans/2026-02-22-process-monitoring-refactoring.review.md` to verify:
- [ ] Target structure shows `Api.cs` + `Internal/` convention
- [ ] No references to `ProcessMonitorModule.StartProcessMonitoringDelegate` (should be `StartProcessMonitoringDelegate`)
- [ ] No references to `using static PerformanceTester.ProcessMonitoring.ProcessMonitorModule;` (should be removed or replaced)
- [ ] All file paths for new files use `Internal/` prefix
- [ ] Namespace `.Internal` is used in code blocks for internal types
- [ ] ProcessCpuCalculator is nested in module, not a separate file
- [ ] ProcessMetrics and ProcessNameExtractor content is in Api.cs, not separate files
- [ ] Files deleted list includes all 6 files (IProcessMonitor, ProcessMonitorService, ProcessMonitorPhaseInfo, ProcessMetrics, ProcessNameExtractor, ProcessCpuCalculator)

**Step 2: Verify the guideline update**

Read `coding-guidelines/05-state-and-composition-patterns.md` to verify:
- [ ] File organization section shows `Api.cs` + `Internal/` as the "Good" example
- [ ] Library vs terminal project scope is documented
- [ ] Namespace convention is documented
- [ ] Old flat structure is shown as an "Avoid" pattern

**Step 3: Commit any fixes found in review**

```bash
git add -A
git commit -m "docs: final review fixes for file structure convention"
```
