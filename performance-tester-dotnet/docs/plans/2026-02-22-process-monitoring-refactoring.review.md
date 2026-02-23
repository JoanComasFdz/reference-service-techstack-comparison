# ProcessMonitoring Functional Refactoring Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Refactor `PerformanceTester.ProcessMonitoring` from interface-based OOP to functional patterns (named delegates, context record, static operations, thin shell) matching the `DockerMonitoring` reference architecture.

**Architecture:** Remove `IProcessMonitor` interface. Extract mutable state into `MonitorContext` record (Guideline 05-04). Extract business logic into `MonitoringOperations` static class. Reduce `ProcessMonitorService` to a thin shell that only owns lifecycle (Guideline 05-06). Expose public API via named delegates registered in DI (Guidelines 02-01 to 02-03). Use value objects for `ProcessMonitorPhaseInfo` fields (Guideline 04-01).

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

### Target Structure (Functional, delegate-based)
```
ProcessMonitoring/
├── ProcessMetrics.cs                     ← KEEP: Data record (public contract, unchanged)
├── ProcessCpuCalculator.cs               ← KEEP: Internal utility (unchanged)
├── ProcessNameExtractor.cs               ← KEEP: Public utility (unchanged)
├── ServiceCollectionExtensions.cs        ← REWRITE: Register delegates instead of interface
├── Monitoring/
│   ├── ProcessMonitoringDelegates.cs     ← NEW: Named delegate definitions
│   ├── ProcessMonitorPhaseInfo.cs        ← MOVE+UPDATE: Use ProcessId & SampleCount VOs
│   ├── ProcessMonitorService.cs          ← MOVE+REWRITE: Thin shell (lifecycle only)
│   ├── MonitorContext.cs                 ← NEW: Mutable state record
│   └── MonitoringOperations.cs           ← NEW: Static operations (sampling loop)
└── ValueObjects/
    └── SampleCount.cs                    ← NEW: NonNegativeInt value object
```

### Files Deleted
```
├── IProcessMonitor.cs                    ← DELETED: Replaced by named delegates
├── ProcessMonitorPhaseInfo.cs (root)     ← DELETED: Moved to Monitoring/
├── ProcessMonitorService.cs (root)       ← DELETED: Moved to Monitoring/
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

## Task 2: Create Monitoring Infrastructure (Delegates, Context, Phase Info)

**Files:**
- Create: `src/PerformanceTester.ProcessMonitoring/Monitoring/ProcessMonitoringDelegates.cs`
- Create: `src/PerformanceTester.ProcessMonitoring/Monitoring/MonitorContext.cs`
- Create: `src/PerformanceTester.ProcessMonitoring/Monitoring/ProcessMonitorPhaseInfo.cs`

**Step 1: Create `Monitoring/ProcessMonitoringDelegates.cs`**

These named delegates replace the `IProcessMonitor` interface. They follow Guideline 02-02 (named delegates over Action/Func) and Guideline 02-06 (Delegate suffix).

```csharp
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.ProcessMonitoring.Monitoring;

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
```

**Step 2: Create `Monitoring/MonitorContext.cs`**

Centralizes all mutable state per Guideline 05-04.

```csharp
using System.Collections.Concurrent;

namespace PerformanceTester.ProcessMonitoring.Monitoring;

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
```

**Step 3: Create `Monitoring/ProcessMonitorPhaseInfo.cs`**

This is the updated version using `ProcessId` and `SampleCount` value objects. It replaces the root-level `ProcessMonitorPhaseInfo.cs`.

```csharp
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.ProcessMonitoring.ValueObjects;

namespace PerformanceTester.ProcessMonitoring.Monitoring;

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
```

**Step 4: Verify it builds**

Run: `dotnet build src/PerformanceTester.ProcessMonitoring`
Expected: Build succeeded, 0 warnings. The old `ProcessMonitorPhaseInfo.cs` at root will cause ambiguity errors. Ignore for now — it gets deleted in Task 6.

Actually — the old and new `ProcessMonitorPhaseInfo` are in different namespaces (`PerformanceTester.ProcessMonitoring` vs `PerformanceTester.ProcessMonitoring.Monitoring`), so both can coexist temporarily. Build should succeed.

**Step 5: Commit**

```bash
git add src/PerformanceTester.ProcessMonitoring/Monitoring/
git commit -m "feat(ProcessMonitoring): add delegates, context record, and updated phase info"
```

---

## Task 3: Create MonitoringOperations (Static Logic)

**Files:**
- Create: `src/PerformanceTester.ProcessMonitoring/Monitoring/MonitoringOperations.cs`

This extracts all business logic from `ProcessMonitorService` into a static class (Guidelines 01-01, 01-02, 05-06). The service will become a thin shell that delegates here.

**Step 1: Create `Monitoring/MonitoringOperations.cs`**

```csharp
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.ProcessMonitoring.ValueObjects;

namespace PerformanceTester.ProcessMonitoring.Monitoring;

/// <summary>
/// Pure static operations for process monitoring.
/// All state access goes through <see cref="MonitorContext"/> parameter (Guideline 01-02).
/// Delegates to <see cref="ProcessCpuCalculator"/> and <see cref="ProcessNameExtractor"/> for calculations.
/// </summary>
internal static class MonitoringOperations
{
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

**Step 2: Verify it builds**

Run: `dotnet build src/PerformanceTester.ProcessMonitoring`
Expected: Build succeeded, 0 warnings

**Step 3: Commit**

```bash
git add src/PerformanceTester.ProcessMonitoring/Monitoring/MonitoringOperations.cs
git commit -m "feat(ProcessMonitoring): add MonitoringOperations static class with sampling logic"
```

---

## Task 4: Create Thin Shell ProcessMonitorService

**Files:**
- Create: `src/PerformanceTester.ProcessMonitoring/Monitoring/ProcessMonitorService.cs`

The thin shell owns the `MonitorContext`, wires `BackgroundService` lifecycle, and exposes public API methods. All business logic is delegated to `MonitoringOperations` (Guideline 05-06).

**Step 1: Create `Monitoring/ProcessMonitorService.cs`**

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.ProcessMonitoring.ValueObjects;

namespace PerformanceTester.ProcessMonitoring.Monitoring;

/// <summary>
/// Thin shell (Guideline 05-06): owns <see cref="MonitorContext"/>, wires lifecycle,
/// delegates all logic to <see cref="MonitoringOperations"/>.
/// </summary>
internal sealed class ProcessMonitorService : BackgroundService
{
    private readonly MonitorContext _ctx = new();
    private readonly TimeSpan _samplingInterval;
    private readonly ILogger<ProcessMonitorService> _logger;

    private bool _started;
    private ProcessId? _processId;
    private ReportProcessMonitorProgressDelegate? _reportProgress;

    public ProcessMonitorService(
        TimeSpan samplingInterval,
        ILogger<ProcessMonitorService> logger)
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

        // Delegate all sampling logic to static operations
        await MonitoringOperations.RunSamplingLoopAsync(
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
git add src/PerformanceTester.ProcessMonitoring/Monitoring/ProcessMonitorService.cs
git commit -m "feat(ProcessMonitoring): add thin shell ProcessMonitorService in Monitoring/"
```

---

## Task 5: Rewrite ServiceCollectionExtensions and Delete Old Files

**Files:**
- Modify: `src/PerformanceTester.ProcessMonitoring/ServiceCollectionExtensions.cs`
- Delete: `src/PerformanceTester.ProcessMonitoring/IProcessMonitor.cs`
- Delete: `src/PerformanceTester.ProcessMonitoring/ProcessMonitorService.cs` (root)
- Delete: `src/PerformanceTester.ProcessMonitoring/ProcessMonitorPhaseInfo.cs` (root)

**Step 1: Rewrite `ServiceCollectionExtensions.cs`**

Replace the triple-registration pattern with delegate-based registration matching DockerMonitoring's pattern.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.ProcessMonitoring.Monitoring;

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
        services.AddSingleton<ProcessMonitorService>(sp =>
        {
            return new ProcessMonitorService(
                interval,
                sp.GetRequiredService<ILogger<ProcessMonitorService>>());
        });

        services.AddSingleton<IHostedService>(sp =>
        {
            return sp.GetRequiredService<ProcessMonitorService>();
        });

        // Register public named delegates
        services.AddSingleton<StartProcessMonitoringDelegate>(sp =>
        {
            var monitor = sp.GetRequiredService<ProcessMonitorService>();
            return monitor.StartMonitoringAsync;
        });

        services.AddSingleton<GetProcessMetricsDelegate>(sp =>
        {
            var monitor = sp.GetRequiredService<ProcessMonitorService>();
            return monitor.GetCollectedMetrics;
        });

        return services;
    }
}
```

**Step 2: Delete old files**

Delete these three files that are replaced by the new `Monitoring/` equivalents:
- `src/PerformanceTester.ProcessMonitoring/IProcessMonitor.cs`
- `src/PerformanceTester.ProcessMonitoring/ProcessMonitorService.cs` (the root one)
- `src/PerformanceTester.ProcessMonitoring/ProcessMonitorPhaseInfo.cs` (the root one)

**Step 3: Verify ProcessMonitoring builds**

Run: `dotnet build src/PerformanceTester.ProcessMonitoring`
Expected: Build succeeded, 0 warnings

**Step 4: Commit**

```bash
git add src/PerformanceTester.ProcessMonitoring/ServiceCollectionExtensions.cs
git rm src/PerformanceTester.ProcessMonitoring/IProcessMonitor.cs
git rm src/PerformanceTester.ProcessMonitoring/ProcessMonitorService.cs
git rm src/PerformanceTester.ProcessMonitoring/ProcessMonitorPhaseInfo.cs
git commit -m "refactor(ProcessMonitoring): replace interface with delegate-based DI registration"
```

---

## Task 6: Update Orchestration Consumers

**Files:**
- Modify: `src/PerformanceTester.Orchestration/Phases/EventTestPhase.cs`
- Modify: `src/PerformanceTester.Orchestration/Phases/ReportingPhase.cs`
- Modify: `src/PerformanceTester.Orchestration/ServiceCollectionExtensions.cs`

**Step 1: Update `EventTestPhase.cs`**

Changes needed:
1. Replace `using PerformanceTester.ProcessMonitoring;` with `using PerformanceTester.ProcessMonitoring.Monitoring;`
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

**After:**
```csharp
var startProcessMonitoring = services.GetRequiredService<ProcessMonitoring.Monitoring.StartProcessMonitoringDelegate>();
// ...
StartProcessMonitoring: (pid) => startProcessMonitoring(pid, _ => { }, ct),
```

Remove the `using PerformanceTester.ProcessMonitoring;` import (no longer needed -- `IProcessMonitor` is gone). Add `using PerformanceTester.ProcessMonitoring.Monitoring;` if not already present (for `StartProcessMonitoringDelegate`). Note: this file also uses `DockerMonitoring.Monitoring.StartDockerMonitoringDelegate` at line 81 which already uses the fully-qualified pattern. Use the same approach for consistency.

**Step 2: Update `ReportingPhase.cs`**

Changes needed:
1. Replace `using PerformanceTester.ProcessMonitoring;` -> keep it (still needed for `ProcessMetrics`)
2. Also add `using PerformanceTester.ProcessMonitoring.Monitoring;`
3. In `BuildDependencies`: resolve `GetProcessMetricsDelegate` from DI instead of `IProcessMonitor`

**Before (lines 85, 93):**
```csharp
var processMonitor = services.GetRequiredService<IProcessMonitor>();
// ...
GetProcessMetrics: processMonitor.GetCollectedMetrics,
```

<!-- applied guideline #01-09: removed unnecessary lambda wrapper around delegate with identical signature -->
**After:**
```csharp
var getProcessMetrics = services.GetRequiredService<ProcessMonitoring.Monitoring.GetProcessMetricsDelegate>();
// ...
GetProcessMetrics: getProcessMetrics,
```

Note: `ReportingPhase.GetProcessMetricsDelegate` (line 32) stays as the phase-level delegate. It returns `IReadOnlyCollection<ProcessMetrics>` -- same signature as ProcessMonitoring's `GetProcessMetricsDelegate`, so the delegate is passed directly without a wrapper lambda (Guideline 01-09).

**Step 3: Update `Orchestration/ServiceCollectionExtensions.cs`**

Remove `using PerformanceTester.ProcessMonitoring;` (line 9) if it was only used for `IProcessMonitor`. Keep the `services.AddProcessMonitoring()` call (line 68) -- it still works but now registers delegates instead of the interface.

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

## Task 7: Update Integration Tests

**Files:**
- Modify: `src/PerformanceTester.ProcessMonitoring.IntegrationTests/Infrastructure/ProcessMonitoring.cs`
- Modify: `src/PerformanceTester.ProcessMonitoring.IntegrationTests/Infrastructure/ProcessMonitoringSystem.cs`
- Modify: `src/PerformanceTester.ProcessMonitoring.IntegrationTests/Infrastructure/ProcessMonitoringAssertions.cs`
- Modify: `src/PerformanceTester.ProcessMonitoring.IntegrationTests/Infrastructure/ProcessMonitorPhaseAwaiter.cs`
- Modify: `src/PerformanceTester.ProcessMonitoring.IntegrationTests/Infrastructure/ProcessMonitorPhaseAwaiterExtensions.cs`
- Modify: `src/PerformanceTester.ProcessMonitoring.IntegrationTests/ProcessMonitorIntegrationTests.cs`

This is the largest task. Read every file in the test project first to understand the current structure, then update them to use the new delegate-based API.

**Key changes across all test files:**

1. **Namespace changes:** `PerformanceTester.ProcessMonitoring` -> add `PerformanceTester.ProcessMonitoring.Monitoring` for phase info, delegates
2. **`IProcessMonitor` -> delegates:** Replace `IProcessMonitor` with `StartProcessMonitoringDelegate` and `GetProcessMetricsDelegate`
3. **`IProgress<ProcessMonitorPhaseInfo>` -> `ReportProcessMonitorProgressDelegate`:** The phase awaiter wraps this delegate
4. **`int ProcessId` -> `ProcessId` value object:** In phase info fields
5. **`int SampleCount` -> `SampleCount` value object:** In phase info fields

**Step 1: Update `Infrastructure/ProcessMonitorPhaseAwaiter.cs`**

Replace `IProgress<ProcessMonitorPhaseInfo>` implementation with `ReportProcessMonitorProgressDelegate`-compatible approach. The awaiter needs to be invocable as a `ReportProcessMonitorProgressDelegate`.

Key change: Instead of implementing `IProgress<ProcessMonitorPhaseInfo>`, expose a method matching the delegate signature. The `Report` method becomes the delegate target.

Read the current file, then update:
- Change the namespace import to `PerformanceTester.ProcessMonitoring.Monitoring`
- Remove `IProgress<ProcessMonitorPhaseInfo>` interface implementation
- Add a `Report` method matching `ReportProcessMonitorProgressDelegate` signature
- Add a `AsDelegate` property or method that returns the delegate
- Update `SampleCount` property to use `SampleCount` value object or remain `int` for test convenience
- Update `ProcessMonitorPhaseInfo` field accesses: `.ProcessId` is now `ProcessId` VO (use `.Value` for int comparison), `.SampleCount` is now `SampleCount` VO (use `.Value` for int comparison)

**Step 2: Update `Infrastructure/ProcessMonitorPhaseAwaiterExtensions.cs`**

- Update namespace imports
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

Update `StartMonitoringAsync` to use delegate:

**Before:**
```csharp
public async Task StartMonitoringAsync(int processId, IProgress<ProcessMonitorPhaseInfo>? progress = null, ...)
{
    await Monitor.StartMonitoringAsync(processId, progress, cancellationToken);
}
```

**After:**
```csharp
public async Task StartMonitoringAsync(
    ProcessId processId,
    ReportProcessMonitorProgressDelegate reportProgress,
    CancellationToken cancellationToken = default)
{
    await StartMonitoring(processId, reportProgress, cancellationToken);
}
```

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

**Step 7: Verify tests build**

Run: `dotnet build src/PerformanceTester.ProcessMonitoring.IntegrationTests`
Expected: Build succeeded, 0 warnings

**Step 8: Commit**

```bash
git add src/PerformanceTester.ProcessMonitoring.IntegrationTests/
git commit -m "refactor(ProcessMonitoring.Tests): update tests to use delegate-based API"
```

---

## Task 8: Full Build and Test Verification

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

The current code uses `List<ProcessMetrics>` + `Lock`. The refactored code uses `ConcurrentBag<DockerMetrics>` (matching DockerMonitoring). ConcurrentBag doesn't maintain insertion order, so `GetCollectedMetrics()` sorts by timestamp on retrieval — same pattern as DockerMonitoring.

### Why Keep ProcessNameExtractor and ProcessCpuCalculator at Root?

These are self-contained utilities with no coupling to the monitoring infrastructure. `ProcessNameExtractor` is public (consumers may use it). `ProcessCpuCalculator` is internal but standalone. Moving them to a subdirectory would add complexity without benefit (Guideline 01-03: inline single-use code, extended to "don't over-organize").
