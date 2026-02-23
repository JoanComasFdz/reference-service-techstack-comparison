# ProcessMonitoring Functional Refactoring Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Refactor `PerformanceTester.ProcessMonitoring` from interface-based OOP to functional patterns (named delegates, context record, static operations, thin shell) matching the `DockerMonitoring` reference architecture.

**Architecture:** Remove `IProcessMonitor` interface. Consolidate all public types (delegates, phase info, data records, utilities) into `Api.cs` at project root. Extract mutable state into `MonitorContext` record (Guideline 05-04). Extract business logic into static operations inside `Internal/ProcessMonitorModule.cs`. Create thin shell `Internal/ProcessMonitorBackgroundService.cs` that only owns lifecycle (Guideline 05-06). Expose public API via named delegates registered in DI (Guidelines 02-01 to 02-03). Use value objects for `ProcessMonitorPhaseInfo` fields (Guideline 04-01).

**Tech Stack:** .NET 9, Microsoft.Extensions.Hosting, PerformanceTester.Infrastructure (value object base classes)

---

## Current State → Target State

### Current Structure (Interface-based)
```
ProcessMonitoring/
├── IProcessMonitor.cs                    ← Interface (public API)
├── ProcessMonitorService.cs              ← BackgroundService implementing IProcessMonitor (331 lines, mixed concerns)
├── ProcessMetrics.cs                     ← Data record
├── ProcessMonitorPhaseInfo.cs            ← Phase enums + record struct (uses raw int)
├── ProcessCpuCalculator.cs               ← Internal utility
├── ProcessNameExtractor.cs               ← Public utility
└── ServiceCollectionExtensions.cs        ← Triple-registers interface
```

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

### Consumer Updates (Orchestration)
```
Orchestration/Phases/EventTestPhase.cs       ← UPDATE: Use StartProcessMonitoringDelegate from ProcessMonitoring
Orchestration/Phases/ReportingPhase.cs       ← UPDATE: Use GetProcessMetricsDelegate from ProcessMonitoring
Orchestration/ServiceCollectionExtensions.cs ← UPDATE: Remove IProcessMonitor references from comments
```

### Test Updates
```
ProcessMonitoring.IntegrationTests/Infrastructure/ProcessMonitoring.cs            ← REWRITE: Use delegates
ProcessMonitoring.IntegrationTests/Infrastructure/ProcessMonitoringSystem.cs      ← UPDATE: Expose delegates
ProcessMonitoring.IntegrationTests/Infrastructure/ProcessMonitoringAssertions.cs  ← REWRITE: Assert on delegates
ProcessMonitoring.IntegrationTests/Infrastructure/ProcessMonitorPhaseAwaiter.cs   ← UPDATE: Use VO types
ProcessMonitoring.IntegrationTests/Infrastructure/ProcessMonitorPhaseAwaiterExtensions.cs ← UPDATE: Use VO types
ProcessMonitoring.IntegrationTests/ProcessMonitorIntegrationTests.cs              ← UPDATE: Use new API
```

---

## Task 1: Add Infrastructure Reference and Create Value Objects

**Files:**
- Modify: `src/PerformanceTester.ProcessMonitoring/PerformanceTester.ProcessMonitoring.csproj`
- Create: `src/PerformanceTester.ProcessMonitoring/ValueObjects/SampleCount.cs`

**Step 1: Add project reference to Infrastructure**

Add to `PerformanceTester.ProcessMonitoring.csproj`:
```xml
<ItemGroup>
  <ProjectReference Include="..\PerformanceTester.Infrastructure\PerformanceTester.Infrastructure.csproj" />
</ItemGroup>
```

This provides access to `NonNegativeInt`, `NonNegativeDouble`, `NonEmptyString` base classes and the existing `ProcessId` value object.

**Step 2: Create `ValueObjects/SampleCount.cs`**

```csharp
using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.ProcessMonitoring.ValueObjects;

/// <summary>
/// Value object representing a non-negative count of collected process metric samples.
/// </summary>
public sealed record SampleCount : NonNegativeInt
{
    private SampleCount(int value) : base(value) { }

    /// <summary>
    /// Creates a SampleCount from an integer. Returns Failure if negative.
    /// </summary>
    <!-- applied guideline #06-04: expression body must start on same line as => -->
    public static Result<SampleCount, string> Create(int value) => Create(value, "Sample count", v => new SampleCount(v));

    /// <summary>
    /// Creates a SampleCount from a known-valid integer (e.g., from collection.Count).
    /// </summary>
    public static SampleCount FromInt(int value) => new(value);
}
```

**Step 3: Verify it builds**

Run: `dotnet build src/PerformanceTester.ProcessMonitoring`
Expected: Build succeeded, 0 warnings

**Step 4: Commit**

```bash
git add src/PerformanceTester.ProcessMonitoring/PerformanceTester.ProcessMonitoring.csproj \
        src/PerformanceTester.ProcessMonitoring/ValueObjects/SampleCount.cs
git commit -m "feat(ProcessMonitoring): add Infrastructure reference and SampleCount value object"
```

---

<!-- applied guideline #05-06: visibility-first file structure — public types (delegates, phase info, ProcessMetrics, ProcessNameExtractor) go to Api.cs at project root; internal types (context record, operations, CPU calculator) go to Internal/ProcessMonitorModule.cs -->
## Task 2: Create Api.cs and Internal/ProcessMonitorModule.cs

**Files:**
- Create: `src/PerformanceTester.ProcessMonitoring/Api.cs`
- Create: `src/PerformanceTester.ProcessMonitoring/Internal/ProcessMonitorModule.cs`

Two files that together replace `IProcessMonitor.cs`, `ProcessMonitorPhaseInfo.cs`, `ProcessMonitorService.cs`, `ProcessMetrics.cs`, `ProcessNameExtractor.cs`, and `ProcessCpuCalculator.cs`. Public types go to `Api.cs` (Guideline 05-06 visibility-first structure). Internal types go to the module file in `Internal/` (Guideline 02-05 reading order: context record → static operations).

**Step 1: Create `Api.cs`**

All public types extracted from the module — delegates, phase info enums/record, ProcessMetrics record, and ProcessNameExtractor. Reading order follows Guideline 02-05: delegates → records/enums → data contracts → utilities.

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

**Step 2: Create `Internal/ProcessMonitorModule.cs`**

Internal types only: context record, CPU calculator (nested), and all static operations.

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

**Step 3: Verify it builds**

Run: `dotnet build src/PerformanceTester.ProcessMonitoring`
Expected: Build succeeded, 0 warnings. The old `ProcessMonitorPhaseInfo.cs`, `ProcessMetrics.cs`, `ProcessNameExtractor.cs`, and `ProcessCpuCalculator.cs` at root will cause ambiguity/duplicate errors — delete them in Task 4.

**Step 4: Commit**

```bash
git add src/PerformanceTester.ProcessMonitoring/Api.cs \
        src/PerformanceTester.ProcessMonitoring/Internal/ProcessMonitorModule.cs
git commit -m "feat(ProcessMonitoring): add Api.cs with public types and Internal/ProcessMonitorModule with operations"
```

---

## Task 3: Create Thin Shell Internal/ProcessMonitorBackgroundService

**Files:**
- Create: `src/PerformanceTester.ProcessMonitoring/Internal/ProcessMonitorBackgroundService.cs`

<!-- applied guideline #05-06: shell file placed in Internal/ directory with .Internal namespace; references public types from Api.cs via using PerformanceTester.ProcessMonitoring -->
The thin shell owns the `ProcessMonitorModule.MonitorContext`, wires `BackgroundService` lifecycle, and exposes public API methods. All business logic is delegated to `ProcessMonitorModule` static methods (Guideline 05-06).

**Step 1: Create `ProcessMonitorBackgroundService.cs`**

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.ProcessMonitoring.Internal;

/// <summary>
/// Thin shell (Guideline 05-06): owns <see cref="ProcessMonitorModule.MonitorContext"/>, wires lifecycle,
/// delegates all logic to <see cref="ProcessMonitorModule"/> static methods.
/// </summary>
internal sealed class ProcessMonitorBackgroundService : BackgroundService
{
    private readonly ProcessMonitorModule.MonitorContext _ctx = new();
    private readonly TimeSpan _samplingInterval;
    private readonly ILogger<ProcessMonitorBackgroundService> _logger;

    private bool _started;
    private ProcessId? _processId;
    private ReportProcessMonitorProgressDelegate? _reportProgress;

    public ProcessMonitorBackgroundService(
        TimeSpan samplingInterval,
        ILogger<ProcessMonitorBackgroundService> logger)
    {
        if (samplingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(samplingInterval),
                samplingInterval,
                "Sampling interval must be positive");
        }

        _samplingInterval = samplingInterval;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // -- Public API (exposed via delegates in DI) ------------------------------------

    /// <summary>
    /// Starts monitoring the specified process.
    /// Blocks until the first sample has been collected.
    /// </summary>
    public async Task StartMonitoringAsync(
        ProcessId processId,
        ReportProcessMonitorProgressDelegate reportProgress,
        CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            throw new InvalidOperationException(
                $"Monitoring has already been started for process {_processId}");
        }

        _started = true;
        _processId = processId;
        _reportProgress = reportProgress;

        reportProgress(ProcessMonitorPhaseInfo.Starting(
            ProcessMonitorPhase.MonitoringRequested,
            processId,
            message: $"Starting monitoring for process {processId}"));

        _ctx.StartSignal.TrySetResult();
        _logger.LogInformation(
            "StartMonitoringAsync called for PID {ProcessId}, waiting for first sample...",
            processId);

        await _ctx.FirstSampleCollected.Task.WaitAsync(cancellationToken);
        _logger.LogInformation("First sample collected for PID {ProcessId}", processId);
    }

    /// <summary>
    /// Returns all collected metrics in chronological order.
    /// </summary>
    public IReadOnlyCollection<ProcessMetrics> GetCollectedMetrics() => _ctx.CollectedMetrics
        .OrderBy(m => m.Timestamp)
        .ToList()
        .AsReadOnly();

    // -- Lifecycle (thin orchestration only) ------------------------------------------

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ProcessMonitor BackgroundService started, waiting for StartMonitoringAsync() call...");

        // Wait for StartMonitoring() to provide process ID
        try
        {
            await _ctx.StartSignal.Task.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "ProcessMonitor stopped before StartMonitoringAsync() was called");
            return;
        }

        // Delegate all sampling logic to module
        await ProcessMonitorModule.RunSamplingLoopAsync(
            _ctx,
            _processId!,
            _samplingInterval,
            _reportProgress!,
            _logger,
            stoppingToken);
    }
}
```

**Step 2: Verify it builds**

Run: `dotnet build src/PerformanceTester.ProcessMonitoring`
Expected: Build succeeded, 0 warnings

**Step 3: Commit**

```bash
git add src/PerformanceTester.ProcessMonitoring/Internal/ProcessMonitorBackgroundService.cs
git commit -m "feat(ProcessMonitoring): add thin shell Internal/ProcessMonitorBackgroundService"
```

---

## Task 4: Rewrite ServiceCollectionExtensions and Delete Old Files

**Files:**
- Modify: `src/PerformanceTester.ProcessMonitoring/ServiceCollectionExtensions.cs`
- Delete: `src/PerformanceTester.ProcessMonitoring/IProcessMonitor.cs`
- Delete: `src/PerformanceTester.ProcessMonitoring/ProcessMonitorService.cs`
- Delete: `src/PerformanceTester.ProcessMonitoring/ProcessMonitorPhaseInfo.cs`
- Delete: `src/PerformanceTester.ProcessMonitoring/ProcessMetrics.cs`
- Delete: `src/PerformanceTester.ProcessMonitoring/ProcessNameExtractor.cs`
- Delete: `src/PerformanceTester.ProcessMonitoring/ProcessCpuCalculator.cs`

**Step 1: Rewrite `ServiceCollectionExtensions.cs`**

Replace the triple-registration pattern with delegate-based registration matching DockerMonitoring's pattern.

<!-- applied guideline #05-06: delegates are now standalone types in PerformanceTester.ProcessMonitoring namespace (from Api.cs); BackgroundService is in .Internal namespace -->
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.ProcessMonitoring.Internal;

namespace PerformanceTester.ProcessMonitoring;

/// <summary>
/// Extension methods for registering ProcessMonitoring services with dependency injection.
/// Registers internal BackgroundService and exposes named delegates publicly.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds process monitoring services.
    /// Registers an internal BackgroundService and two public named delegates:
    /// <see cref="StartProcessMonitoringDelegate"/> and <see cref="GetProcessMetricsDelegate"/>.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="samplingInterval">Sampling interval (default: 500ms).</param>
    /// <returns>Service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The BackgroundService starts automatically when IHost.StartAsync() is called but
    /// waits for <see cref="StartProcessMonitoringDelegate"/> to be invoked with a process ID.
    /// This deferred start pattern supports orchestration scenarios where the process ID
    /// is not known at DI registration time.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddProcessMonitoring(
        this IServiceCollection services,
        TimeSpan? samplingInterval = null)
    {
        var interval = samplingInterval ?? TimeSpan.FromMilliseconds(500);

        // Register BackgroundService (internal, not exposed)
        services.AddSingleton<ProcessMonitorBackgroundService>(sp =>
            new ProcessMonitorBackgroundService(
                interval,
                sp.GetRequiredService<ILogger<ProcessMonitorBackgroundService>>()));

        services.AddSingleton<IHostedService>(sp =>
            sp.GetRequiredService<ProcessMonitorBackgroundService>());

        // Register public named delegates (standalone in PerformanceTester.ProcessMonitoring namespace)
        services.AddSingleton<StartProcessMonitoringDelegate>(sp =>
        {
            var monitor = sp.GetRequiredService<ProcessMonitorBackgroundService>();
            return monitor.StartMonitoringAsync;
        });

        services.AddSingleton<GetProcessMetricsDelegate>(sp =>
        {
            var monitor = sp.GetRequiredService<ProcessMonitorBackgroundService>();
            return monitor.GetCollectedMetrics;
        });

        return services;
    }
}
```

<!-- applied guideline #01-09: removed useless { return ...; } wrapper bodies from single-expression lambdas — the AddSingleton<ProcessMonitorBackgroundService> and AddSingleton<IHostedService> factories each perform a single expression and gain nothing from block syntax -->

**Step 2: Delete old files**

Delete these six files that are replaced by Api.cs, Internal/ProcessMonitorModule.cs, and Internal/ProcessMonitorBackgroundService.cs:
- `src/PerformanceTester.ProcessMonitoring/IProcessMonitor.cs`
- `src/PerformanceTester.ProcessMonitoring/ProcessMonitorService.cs`
- `src/PerformanceTester.ProcessMonitoring/ProcessMonitorPhaseInfo.cs`
- `src/PerformanceTester.ProcessMonitoring/ProcessMetrics.cs`
- `src/PerformanceTester.ProcessMonitoring/ProcessNameExtractor.cs`
- `src/PerformanceTester.ProcessMonitoring/ProcessCpuCalculator.cs`

**Step 3: Verify ProcessMonitoring builds**

Run: `dotnet build src/PerformanceTester.ProcessMonitoring`
Expected: Build succeeded, 0 warnings

**Step 4: Commit**

```bash
git add src/PerformanceTester.ProcessMonitoring/ServiceCollectionExtensions.cs
git rm src/PerformanceTester.ProcessMonitoring/IProcessMonitor.cs
git rm src/PerformanceTester.ProcessMonitoring/ProcessMonitorService.cs
git rm src/PerformanceTester.ProcessMonitoring/ProcessMonitorPhaseInfo.cs
git rm src/PerformanceTester.ProcessMonitoring/ProcessMetrics.cs
git rm src/PerformanceTester.ProcessMonitoring/ProcessNameExtractor.cs
git rm src/PerformanceTester.ProcessMonitoring/ProcessCpuCalculator.cs
git commit -m "refactor(ProcessMonitoring): rewrite DI registration for standalone delegates, delete old files"
```

---

## Task 5: Update Orchestration Consumers

**Files:**
- Modify: `src/PerformanceTester.Orchestration/Phases/EventTestPhase.cs`
- Modify: `src/PerformanceTester.Orchestration/Phases/ReportingPhase.cs`
- Modify: `src/PerformanceTester.Orchestration/ServiceCollectionExtensions.cs`

**Step 1: Update `EventTestPhase.cs`**

Changes needed:
1. Remove `using PerformanceTester.ProcessMonitoring;` if it was only used for `IProcessMonitor` — keep it if also needed for other types in that namespace
2. In `BuildDependencies`: resolve `StartProcessMonitoringDelegate` from DI instead of `IProcessMonitor`
3. Adapt the delegate (bake in cancellation token, discard progress)

The `EventTestPhase.StartProcessMonitoringDelegate` (line 31) stays -- it's the phase-level delegate with a simpler signature. The adaptation from ProcessMonitoring's richer delegate happens in `BuildDependencies`.

Replace the `BuildDependencies` method body. The key change is lines 80 and 87:

**Before:**
```csharp
var processMonitor = services.GetRequiredService<IProcessMonitor>();
// ...
StartProcessMonitoring: (pid) => processMonitor.StartMonitoringAsync(pid.Value, cancellationToken: ct),
```

<!-- applied guideline #05-06: delegates are now standalone types in PerformanceTester.ProcessMonitoring namespace (from Api.cs) -->
**After:**
```csharp
var startProcessMonitoring = services.GetRequiredService<StartProcessMonitoringDelegate>();
// ...
StartProcessMonitoring: (pid) => startProcessMonitoring(pid, _ => { }, ct),
```

Keep `using PerformanceTester.ProcessMonitoring;` — the delegates are standalone types in the `PerformanceTester.ProcessMonitoring` namespace (from `Api.cs`), so they are accessed directly as `StartProcessMonitoringDelegate`.

**Step 2: Update `ReportingPhase.cs`**

Changes needed:
1. `using PerformanceTester.ProcessMonitoring;` — keep it (still needed for `ProcessMetrics` and now also for `GetProcessMetricsDelegate`)
2. In `BuildDependencies`: resolve `GetProcessMetricsDelegate` from DI instead of `IProcessMonitor`

**Before (lines 85, 93):**
```csharp
var processMonitor = services.GetRequiredService<IProcessMonitor>();
// ...
GetProcessMetrics: processMonitor.GetCollectedMetrics,
```

<!-- applied guideline #01-09: removed unnecessary lambda wrapper around delegate with identical signature -->
<!-- applied guideline #05-06: delegates are now standalone types in PerformanceTester.ProcessMonitoring namespace (from Api.cs) -->
**After:**
```csharp
var getProcessMetrics = services.GetRequiredService<GetProcessMetricsDelegate>();
// ...
GetProcessMetrics: getProcessMetrics,
```

Note: `ReportingPhase.GetProcessMetricsDelegate` (line 32) stays as the phase-level delegate. It returns `IReadOnlyCollection<ProcessMetrics>` -- same signature as ProcessMonitoring's `GetProcessMetricsDelegate`, so the delegate is passed directly without a wrapper lambda (Guideline 01-09).

**Step 3: Update `Orchestration/ServiceCollectionExtensions.cs`**

Keep `using PerformanceTester.ProcessMonitoring;` (line 9) — it still works and `AddProcessMonitoring()` is in that namespace. Keep the `services.AddProcessMonitoring()` call (line 68) -- it still works but now registers delegates instead of the interface.

Update the XML comment (lines 40-43) that mentions `IProcessMonitor`:

**Before:**
```csharp
/// ProcessMonitoring uses deferred start pattern - the process ID is provided after IHost.StartAsync()
/// via IProcessMonitor.StartMonitoringAsync(processId).
```

**After:**
```csharp
/// ProcessMonitoring uses deferred start pattern - the process ID is provided after IHost.StartAsync()
/// via StartProcessMonitoringDelegate.
```

**Step 4: Verify Orchestration builds**

Run: `dotnet build src/PerformanceTester.Orchestration`
Expected: Build succeeded, 0 warnings

**Step 5: Commit**

```bash
git add src/PerformanceTester.Orchestration/Phases/EventTestPhase.cs \
        src/PerformanceTester.Orchestration/Phases/ReportingPhase.cs \
        src/PerformanceTester.Orchestration/ServiceCollectionExtensions.cs
git commit -m "refactor(Orchestration): consume ProcessMonitoring via delegates instead of IProcessMonitor"
```

---

## Task 6: Update Integration Tests

**Files:**
- Modify: `src/PerformanceTester.ProcessMonitoring.IntegrationTests/Infrastructure/ProcessMonitoring.cs`
- Modify: `src/PerformanceTester.ProcessMonitoring.IntegrationTests/Infrastructure/ProcessMonitoringSystem.cs`
- Modify: `src/PerformanceTester.ProcessMonitoring.IntegrationTests/Infrastructure/ProcessMonitoringAssertions.cs`
- Modify: `src/PerformanceTester.ProcessMonitoring.IntegrationTests/Infrastructure/ProcessMonitorPhaseAwaiter.cs`
- Modify: `src/PerformanceTester.ProcessMonitoring.IntegrationTests/Infrastructure/ProcessMonitorPhaseAwaiterExtensions.cs`
- Modify: `src/PerformanceTester.ProcessMonitoring.IntegrationTests/ProcessMonitorIntegrationTests.cs`

This is the largest task. Read every file in the test project first to understand the current structure, then update them to use the new delegate-based API.

**Key changes across all test files:**

<!-- applied guideline #05-06: public types (delegates, phase info, data records) are standalone in PerformanceTester.ProcessMonitoring namespace (from Api.cs); internal types are in PerformanceTester.ProcessMonitoring.Internal -->
1. **Namespace changes:** Public types (delegates, phase info, ProcessMetrics) are standalone in `PerformanceTester.ProcessMonitoring` namespace (from `Api.cs`). Use `using PerformanceTester.ProcessMonitoring;` to access all public types directly
2. **`IProcessMonitor` -> delegates:** Replace `IProcessMonitor` with `StartProcessMonitoringDelegate` and `GetProcessMetricsDelegate`
3. **`IProgress<ProcessMonitorPhaseInfo>` -> `ReportProcessMonitorProgressDelegate`:** The phase awaiter wraps this delegate
4. **`int ProcessId` -> `ProcessId` value object:** In phase info fields
5. **`int SampleCount` -> `SampleCount` value object:** In phase info fields

**Step 1: Update `Infrastructure/ProcessMonitorPhaseAwaiter.cs`**

Replace `IProgress<ProcessMonitorPhaseInfo>` implementation with `ReportProcessMonitorProgressDelegate`-compatible approach. The awaiter needs to be invocable as a `ReportProcessMonitorProgressDelegate`.

Key change: Instead of implementing `IProgress<ProcessMonitorPhaseInfo>`, expose a method matching the delegate signature. The `Report` method becomes the delegate target.

Read the current file, then update:
- Add `using PerformanceTester.ProcessMonitoring;` to import public types from Api.cs
- Remove `IProgress<ProcessMonitorPhaseInfo>` interface implementation
- Add a `Report` method matching `ReportProcessMonitorProgressDelegate` signature
- Add a `AsDelegate` property or method that returns the delegate
- Update `SampleCount` property to use `SampleCount` value object or remain `int` for test convenience
- Update `ProcessMonitorPhaseInfo` field accesses: `.ProcessId` is now `ProcessId` VO (use `.Value` for int comparison), `.SampleCount` is now `SampleCount` VO (use `.Value` for int comparison)

**Step 2: Update `Infrastructure/ProcessMonitorPhaseAwaiterExtensions.cs`**

- Update namespace imports (add `using PerformanceTester.ProcessMonitoring;`)
- Update `ProcessMonitorPhaseInfo` field accesses (`.ProcessId.Value` instead of `.ProcessId`, `.SampleCount.Value` instead of `.SampleCount`)

**Step 3: Update `Infrastructure/ProcessMonitoring.cs`** (the test facade)

Replace `IProcessMonitor` with delegate-based API:

**Before:**
```csharp
public IProcessMonitor Monitor { get; private set; } = null!;
// ...
Monitor = _host.Services.GetRequiredService<IProcessMonitor>();
```

**After:**
```csharp
public StartProcessMonitoringDelegate StartMonitoring { get; private set; } = null!;
public GetProcessMetricsDelegate GetMetrics { get; private set; } = null!;
// ...
StartMonitoring = _host.Services.GetRequiredService<StartProcessMonitoringDelegate>();
GetMetrics = _host.Services.GetRequiredService<GetProcessMetricsDelegate>();
```

<!-- applied guideline #01-09: removed the StartMonitoringAsync wrapper method — it forwarded all parameters unchanged to the StartMonitoring delegate, adding no value; callers invoke StartMonitoring directly -->
Remove the `StartMonitoringAsync` wrapper method entirely. Callers in `ProcessMonitorIntegrationTests.cs` call `StartMonitoring(...)` directly on the facade.

**Step 4: Update `Infrastructure/ProcessMonitoringSystem.cs`**

Minor updates to expose the new delegate-based facade.

**Step 5: Update `Infrastructure/ProcessMonitoringAssertions.cs`**

Replace `AssertingThat<IProcessMonitor>` with `AssertingThat<GetProcessMetricsDelegate>` or adapt assertions to use the delegate directly.

**Before:**
```csharp
public static AssertingThat<IProcessMonitor> HasCollectedMetrics(
    this AssertingThat<IProcessMonitor> assertingThat)
{
    var metrics = assertingThat.InstanceToAssert.GetCollectedMetrics();
    Assert.NotEmpty(metrics);
    return assertingThat;
}
```

**After (adapt to work with the facade or delegates):**
Assertions should target the `ProcessMonitoring` facade (the test infrastructure class) or the `GetProcessMetricsDelegate` directly. Review the existing test patterns to determine the best approach.

**Step 6: Update `ProcessMonitorIntegrationTests.cs`**

Update test methods to use the new API:
- `int processId` -> `ProcessId.FromInt(processId)`
- `System.ProcessMonitoring.Monitor.GetCollectedMetrics()` -> `System.ProcessMonitoring.GetMetrics()`
- Phase awaiter: pass as delegate instead of `IProgress<T>`
- `await System.ProcessMonitoring.StartMonitoringAsync(...)` -> `await System.ProcessMonitoring.StartMonitoring(...)`

**Step 7: Verify tests build**

Run: `dotnet build src/PerformanceTester.ProcessMonitoring.IntegrationTests`
Expected: Build succeeded, 0 warnings

**Step 8: Commit**

```bash
git add src/PerformanceTester.ProcessMonitoring.IntegrationTests/
git commit -m "refactor(ProcessMonitoring.Tests): update tests to use delegate-based API"
```

---

## Task 7: Full Build and Test Verification

**Step 1: Build entire solution**

Run: `dotnet build /workspace/performance-tester-dotnet/PerformanceTester.sln`
Expected: Build succeeded, 0 warnings across all 25 projects

If there are compile errors, fix them. Common issues:
- Missing `using` statements for new namespaces
- `ProcessId` vs `int` type mismatches at boundaries
- `SampleCount` vs `int` in phase info consumers

**Step 2: Run ProcessMonitoring tests**

Run: `dotnet test src/PerformanceTester.ProcessMonitoring.IntegrationTests --logger "console;verbosity=detailed"`
Expected: All 6 tests pass

**Step 3: Run full test suite**

Run: `dotnet test /workspace/performance-tester-dotnet/PerformanceTester.sln --logger "console;verbosity=detailed"`
Expected: All tests pass across all test projects

**Step 4: Final commit (if any fixes were needed)**

```bash
git add -A
git commit -m "fix(ProcessMonitoring): resolve build/test issues from functional refactoring"
```

---

## Appendix: Decisions and Trade-offs

### Why No Value Objects for ProcessMetrics Fields?

`ProcessMetrics` is the public output record consumed by Reporting and Orchestration. Keeping it primitive-typed avoids breaking consumers:
```csharp
// KEEP — primitive fields, no change
public record ProcessMetrics(
    DateTimeOffset Timestamp,
    int ProcessId,
    string ProcessName,
    double CpuPercent,
    double MemoryMB,
    int ThreadCount);
```

Value objects are used where they add type safety (phase info fields, delegate parameters) but not in data transfer records that cross slice boundaries.

### Why Use ProcessId from Infrastructure Instead of Creating One?

`ProcessId` is a cross-cutting concept used by Infrastructure (service discovery), Orchestration, and ProcessMonitoring. Infrastructure already owns it. Creating a duplicate would violate DRY and force conversion at boundaries.

### Why ConcurrentBag Instead of List+Lock?

The current code uses `List<ProcessMetrics>` + `Lock`. The refactored code uses `ConcurrentBag<ProcessMetrics>` (matching DockerMonitoring's pattern). ConcurrentBag doesn't maintain insertion order, so `GetCollectedMetrics()` sorts by timestamp on retrieval — same pattern as DockerMonitoring.

### Why Move ProcessNameExtractor to Api.cs and ProcessCpuCalculator to Internal/ProcessMonitorModule.cs?

The visibility-first convention (Guideline 05-06) dictates placement by access level. `ProcessNameExtractor` is public (consumers may use it), so it belongs in `Api.cs` alongside other public types. `ProcessCpuCalculator` is internal and only used by the sampling operations, so it belongs nested inside `ProcessMonitorModule` in `Internal/` — co-located with its only consumer.
