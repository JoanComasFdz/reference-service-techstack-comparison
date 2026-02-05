# TUI Progress Display Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the simple spinner with a multi-phase progress display showing real-time progress bars for event processing and API testing phases, with status emojis for completion states.

**Architecture:** Use functional patterns with static toolbox classes for pure rendering logic. The `ProgressReporter` class maintains minimal state (current phase data) while delegating all rendering to static pure functions. Wire the existing `IProgress<PhaseInfo>` from orchestrator for real-time progress updates.

**Tech Stack:** .NET 9, System.Console for TUI rendering, existing VSA slice patterns

**Design Principles Applied (from CODING_GUIDELINES.md):**
- Static classes for pure rendering logic (no instance state needed)
- Explicit parameters at call sites (no hidden configuration objects)
- Toolbox pattern for small, reusable rendering functions
- Vertical slice ownership (each file tells its complete story)

---

## Problem Analysis

### Current Behavior Issues

1. **No progress information**: The spinner shows only "Running test..." with no indication of which phase is active or how much progress has been made
2. **Spinner position not fixed on completion**: When the test finishes, the spinner character (`|`, `/`, `-`, `\`) remains at whatever frame was last displayed
3. **Missing newline after k6 script creation**: Log messages from k6 appear on the same line as the spinner
4. **No phase-specific progress**: Event count (X/Y events) and API request count are not displayed during execution
5. **No completion status emojis**: Success (✓), cancellation (⊘), or failure (✗) states not indicated

### Existing Infrastructure

The codebase has progress reporting infrastructure that's **not being used by the CLI**:

1. **`ITestOrchestrator.RunTestAsync`** accepts `IProgress<PhaseInfo>? progress` parameter
2. **`PhaseInfo`** record contains phase, state, and message information
3. **`TestCommand.ExecuteAsync`** does NOT pass a progress parameter to `RunTestAsync`
4. **`IEventConsumer.StartTrackingEventsAsync`** accepts `IProgress<ConsumerPhaseInfo>?` with `EventCount` field

---

## Design

### Phase Display Format

```
=== Running Performance Test ===

  [1/3] Setup              ✓ Complete
  [2/3] Event Processing   ████████████████████  10000/10000 events  ✓ Complete
  [3/3] API Load Testing   ████████░░░░░░░░░░░░  15.2s/30.0s (1523 req)  ...
```

### Status Emojis

| State | Emoji | Description |
|-------|-------|-------------|
| In Progress | `...` | Phase is currently executing |
| Complete | `✓` | Phase completed successfully |
| Failed | `✗` | Phase failed with error |
| Cancelled | `⊘` | Phase was cancelled (Ctrl+C) |

### Progress Bar

- Width: 20 characters
- Filled: `█` (U+2588)
- Empty: `░` (U+2591)
- Updates every 100ms (same as current spinner)

---

## File Structure

Following CODING_GUIDELINES.md: Static classes for pure logic, vertical slice ownership.

```
src/PerformanceTester.Cli/Output/
├── IProgressReporter.cs          # MODIFY: Simplified interface (DI justified)
├── ProgressReporter.cs           # MODIFY: Instance class (has state)
├── TestProgress.cs               # CREATE: Plain data record
├── PhaseStatus.cs                # CREATE: Enum + ProgressToolbox static class
├── ProgressLineRenderer.cs       # CREATE: Static class for line rendering
├── PhaseInfoConverter.cs         # CREATE: Static class for PhaseInfo conversion
├── ProgressMessageParser.cs      # CREATE: Static class for message parsing
└── OrchestratorProgressAdapter.cs # CREATE: Instance class (has tracking state)

src/PerformanceTester.ApiLoadTesting/
└── ApiLoadProgress.cs            # CREATE: Plain data record
```

**Design Notes:**
- Static classes: `ProgressToolbox`, `ProgressLineRenderer`, `PhaseInfoConverter`, `ProgressMessageParser`
- Instance classes: `ProgressReporter` (tracks phase state), `OrchestratorProgressAdapter` (tracks counts)
- Interfaces: `IProgressReporter` only (justified for DI)

---

## Tasks

### Task 1: Create PhaseStatus Enum and ProgressToolbox

**Files:**
- Create: `src/PerformanceTester.Cli/Output/PhaseStatus.cs`

**Step 1: Write the enum and static toolbox**

Following CODING_GUIDELINES.md: Static class for pure functions, explicit parameters.

```csharp
namespace PerformanceTester.Cli.Output;

/// <summary>
/// Represents the completion status of a test phase.
/// </summary>
public enum PhaseStatus
{
    /// <summary>Phase has not started yet.</summary>
    Pending,

    /// <summary>Phase is currently executing.</summary>
    InProgress,

    /// <summary>Phase completed successfully.</summary>
    Completed,

    /// <summary>Phase failed with an error.</summary>
    Failed,

    /// <summary>Phase was cancelled by user (Ctrl+C).</summary>
    Cancelled
}

/// <summary>
/// Pure functions for progress display rendering.
/// Static class - no instance state needed (CODING_GUIDELINES: Static Classes for Pure Logic).
/// </summary>
public static class ProgressToolbox
{
    private const int ProgressBarWidth = 20;
    private const char FilledChar = '\u2588';  // █
    private const char EmptyChar = '\u2591';   // ░

    /// <summary>
    /// Gets the status emoji for the given phase status.
    /// </summary>
    public static string GetStatusEmoji(PhaseStatus status) => status switch
    {
        PhaseStatus.Pending => " ",
        PhaseStatus.InProgress => "...",
        PhaseStatus.Completed => "\u2713",  // ✓
        PhaseStatus.Failed => "\u2717",     // ✗
        PhaseStatus.Cancelled => "\u2298",  // ⊘
        _ => " "
    };

    /// <summary>
    /// Renders a progress bar string with explicit width and fill percentage.
    /// </summary>
    public static string RenderProgressBar(double percent, int width = ProgressBarWidth)
    {
        var filledCount = (int)Math.Round(percent / 100 * width);
        var emptyCount = width - filledCount;
        return new string(FilledChar, filledCount) + new string(EmptyChar, emptyCount);
    }

    /// <summary>
    /// Formats event progress as "current/total events".
    /// </summary>
    public static string FormatEventProgress(int current, int total)
        => $"{current:N0}/{total:N0} events";

    /// <summary>
    /// Formats time progress as "elapsed/total" with optional suffix.
    /// </summary>
    public static string FormatTimeProgress(double elapsedSeconds, double totalSeconds, string? suffix = null)
    {
        var result = $"{elapsedSeconds:F1}/{totalSeconds:F1}s";
        return suffix != null ? $"{result} ({suffix})" : result;
    }

    /// <summary>
    /// Calculates progress percentage (0-100), clamped.
    /// </summary>
    public static double CalculatePercent(double current, double total)
        => total > 0 ? Math.Min(100, (current / total) * 100) : 0;

    /// <summary>
    /// Moves cursor up N lines and clears each line (ANSI escape sequences).
    /// </summary>
    public static void ClearPreviousLines(int lineCount)
    {
        for (var i = 0; i < lineCount; i++)
        {
            Console.Write("\x1b[1A"); // Move cursor up one line
            Console.Write("\x1b[2K"); // Clear the entire line
        }
    }
}
```

**Step 2: Verify build**

Run: `dotnet build src/PerformanceTester.Cli`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/PerformanceTester.Cli/Output/PhaseStatus.cs
git commit -m "$(cat <<'EOF'
feat(cli): add PhaseStatus enum and ProgressToolbox

- PhaseStatus enum: Pending, InProgress, Completed, Failed, Cancelled
- ProgressToolbox: static class with pure rendering functions
  - GetStatusEmoji, RenderProgressBar, FormatEventProgress
  - FormatTimeProgress, CalculatePercent, ClearPreviousLines

Follows CODING_GUIDELINES: static class for pure functions.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Create TestProgress Record

**Files:**
- Create: `src/PerformanceTester.Cli/Output/TestProgress.cs`

**Step 1: Write the record**

Following CODING_GUIDELINES.md: Plain data record, no factory methods that hide details.
All construction is explicit at the call site.

```csharp
namespace PerformanceTester.Cli.Output;

/// <summary>
/// Represents progress information for a test phase.
/// Plain data record - construction is explicit at call sites (CODING_GUIDELINES: Explicit Over Implicit).
/// </summary>
/// <param name="PhaseName">Display name of the phase (e.g., "Event Processing").</param>
/// <param name="PhaseNumber">Current phase number (1-based).</param>
/// <param name="TotalPhases">Total number of phases.</param>
/// <param name="Status">Current status of the phase.</param>
/// <param name="Current">Current progress value (events processed, seconds elapsed, etc.).</param>
/// <param name="Total">Total expected value (total events, total duration, etc.).</param>
/// <param name="Unit">Unit of measurement (e.g., "events", "s").</param>
/// <param name="Message">Optional status message (e.g., "1523 req" for API phase).</param>
public readonly record struct TestProgress(
    string PhaseName,
    int PhaseNumber,
    int TotalPhases,
    PhaseStatus Status,
    double Current = 0,
    double Total = 0,
    string? Unit = null,
    string? Message = null);

// Usage is explicit at call sites - no hidden factory methods:
//
// new TestProgress("Setup", 1, 4, PhaseStatus.InProgress)
// new TestProgress("Event Processing", 2, 4, PhaseStatus.InProgress, current: 5000, total: 10000, unit: "events")
// new TestProgress("API Load Testing", 3, 4, PhaseStatus.InProgress, current: 15.2, total: 30.0, unit: "s", message: "1523 req")
```

**Step 2: Verify build**

Run: `dotnet build src/PerformanceTester.Cli`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/PerformanceTester.Cli/Output/TestProgress.cs
git commit -m "$(cat <<'EOF'
feat(cli): add TestProgress record for phase progress data

Provides structured progress information including phase name, current/total
values, and factory methods for each test phase.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Create ProgressLineRenderer Static Class

**Files:**
- Create: `src/PerformanceTester.Cli/Output/ProgressLineRenderer.cs`

**Step 1: Write the renderer**

Following CODING_GUIDELINES.md: Static class with pure functions, uses ProgressToolbox explicitly.

```csharp
using System.Text;

namespace PerformanceTester.Cli.Output;

/// <summary>
/// Renders progress lines for test phases.
/// Static class - no instance state (CODING_GUIDELINES: Static Classes for Pure Logic).
/// Uses ProgressToolbox for small reusable functions (CODING_GUIDELINES: Toolbox Pattern).
/// </summary>
internal static class ProgressLineRenderer
{
    /// <summary>
    /// Renders a complete progress line for a test phase.
    /// All formatting logic is explicit here - vertical slice ownership.
    /// </summary>
    public static string Render(
        string phaseName,
        int phaseNumber,
        int totalPhases,
        PhaseStatus status,
        double current = 0,
        double total = 0,
        string? unit = null,
        string? message = null)
    {
        var sb = new StringBuilder();

        // Phase number prefix: "  [1/4] "
        sb.Append($"  [{phaseNumber}/{totalPhases}] ");

        // Phase name (left-padded to 18 chars for alignment)
        sb.Append($"{phaseName,-18}");

        // Status-dependent content - all logic visible here
        switch (status)
        {
            case PhaseStatus.Pending:
                sb.Append("   Pending");
                break;

            case PhaseStatus.InProgress:
                RenderInProgressContent(sb, current, total, unit, message);
                sb.Append("  ...");
                break;

            case PhaseStatus.Completed:
                RenderCompletedContent(sb, total, unit, message);
                sb.Append($"  {ProgressToolbox.GetStatusEmoji(PhaseStatus.Completed)} Complete");
                break;

            case PhaseStatus.Failed:
                sb.Append($"  {ProgressToolbox.GetStatusEmoji(PhaseStatus.Failed)} Failed");
                if (!string.IsNullOrEmpty(message))
                {
                    sb.Append($": {message}");
                }
                break;

            case PhaseStatus.Cancelled:
                sb.Append($"  {ProgressToolbox.GetStatusEmoji(PhaseStatus.Cancelled)} Cancelled");
                break;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders from TestProgress record for convenience.
    /// Explicit delegation - no hidden behavior.
    /// </summary>
    public static string Render(TestProgress progress)
        => Render(
            progress.PhaseName,
            progress.PhaseNumber,
            progress.TotalPhases,
            progress.Status,
            progress.Current,
            progress.Total,
            progress.Unit,
            progress.Message);

    private static void RenderInProgressContent(
        StringBuilder sb,
        double current,
        double total,
        string? unit,
        string? message)
    {
        if (total <= 0) return;

        // Progress bar using toolbox
        var percent = ProgressToolbox.CalculatePercent(current, total);
        sb.Append(ProgressToolbox.RenderProgressBar(percent));
        sb.Append("  ");

        // Progress values - pattern matching for unit type
        sb.Append(unit switch
        {
            "events" => ProgressToolbox.FormatEventProgress((int)current, (int)total),
            "s" => ProgressToolbox.FormatTimeProgress(current, total, message),
            _ => $"{current:F0}/{total:F0}"
        });
    }

    private static void RenderCompletedContent(
        StringBuilder sb,
        double total,
        string? unit,
        string? message)
    {
        if (total <= 0) return;

        // Full progress bar
        sb.Append(ProgressToolbox.RenderProgressBar(100));
        sb.Append("  ");

        // Final values - pattern matching for unit type
        sb.Append(unit switch
        {
            "events" => ProgressToolbox.FormatEventProgress((int)total, (int)total),
            "s" => ProgressToolbox.FormatTimeProgress(total, total, message),
            _ => $"{total:F0}/{total:F0}"
        });
    }
}
```

**Step 2: Verify build**

Run: `dotnet build src/PerformanceTester.Cli`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/PerformanceTester.Cli/Output/ProgressLineRenderer.cs
git commit -m "$(cat <<'EOF'
feat(cli): add ProgressLineRenderer for phase progress rendering

Static class with pure rendering functions:
- Render() with explicit parameters for all values
- Uses ProgressToolbox for progress bar and formatting
- Supports events (count) and time (seconds) formats

Follows CODING_GUIDELINES: static class, explicit parameters, toolbox pattern.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Update IProgressReporter Interface

**Files:**
- Modify: `src/PerformanceTester.Cli/Output/IProgressReporter.cs`

**Step 1: Read current file**

Read the current `IProgressReporter.cs` file to understand existing interface.

**Step 2: Update the interface**

Following CODING_GUIDELINES.md: Interface is justified here for DI/testing.
Keep it minimal - only methods that need polymorphism.

```csharp
namespace PerformanceTester.Cli.Output;

/// <summary>
/// Reports test progress to the console.
/// Interface justified for DI registration and testing (CODING_GUIDELINES: Composition Over Interfaces -
/// "Don't create interfaces just for the sake of abstraction" - but DI requires it here).
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// Initializes the progress display. Call before reporting progress.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Reports progress for a phase. Updates are rendered immediately.
    /// </summary>
    void ReportProgress(TestProgress progress);

    /// <summary>
    /// Marks current phase with final status (Completed, Failed, or Cancelled).
    /// </summary>
    void SetPhaseStatus(PhaseStatus status, string? message = null);

    /// <summary>
    /// Finalizes display. Ensures proper line termination.
    /// </summary>
    void Finalize();
}
```

**Step 3: Verify build**

Run: `dotnet build src/PerformanceTester.Cli`
Expected: Build failed (ProgressReporter doesn't implement new interface yet) - this is expected

**Step 4: Commit interface changes**

```bash
git add src/PerformanceTester.Cli/Output/IProgressReporter.cs
git commit -m "$(cat <<'EOF'
feat(cli): update IProgressReporter for multi-phase progress

Extends interface to support:
- IProgress<TestProgress> for integration with orchestrator
- Initialize/Finalize lifecycle methods
- Phase completion methods (Complete, Fail, Cancel)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Rewrite ProgressReporter Implementation

**Files:**
- Modify: `src/PerformanceTester.Cli/Output/ProgressReporter.cs`

**Step 1: Read current file**

Read the current `ProgressReporter.cs` to understand existing implementation.

**Step 2: Rewrite the implementation**

Following CODING_GUIDELINES.md:
- Instance class (has state: _phases, _currentPhaseNumber) - not static
- Delegates rendering to static ProgressLineRenderer (toolbox pattern)
- Uses ProgressToolbox for ANSI cursor operations

```csharp
namespace PerformanceTester.Cli.Output;

/// <summary>
/// Progress reporter that shows multi-phase progress with progress bars and status emojis.
/// Instance class - has mutable state (CODING_GUIDELINES: Static Classes for Pure Logic - this has state).
/// Delegates all rendering to static classes (CODING_GUIDELINES: Toolbox Pattern).
/// </summary>
public sealed class ProgressReporter : IProgressReporter
{
    private readonly object _lock = new();
    private readonly Dictionary<int, TestProgress> _phases = new();
    private int _currentPhaseNumber;
    private int _lastRenderedLineCount;
    private bool _isInitialized;
    private bool _isFinalized;

    public void Initialize()
    {
        lock (_lock)
        {
            if (_isInitialized) return;

            _phases.Clear();
            _currentPhaseNumber = 0;
            _lastRenderedLineCount = 0;
            _isInitialized = true;
            _isFinalized = false;

            Console.WriteLine(); // Start progress section
        }
    }

    public void ReportProgress(TestProgress progress)
    {
        lock (_lock)
        {
            if (!_isInitialized || _isFinalized) return;

            _phases[progress.PhaseNumber] = progress;

            // Track current phase (highest in-progress phase)
            if (progress.Status == PhaseStatus.InProgress && progress.PhaseNumber > _currentPhaseNumber)
            {
                _currentPhaseNumber = progress.PhaseNumber;
            }

            RenderAllPhases();
        }
    }

    public void SetPhaseStatus(PhaseStatus status, string? message = null)
    {
        lock (_lock)
        {
            if (!_isInitialized || _isFinalized) return;
            if (!_phases.TryGetValue(_currentPhaseNumber, out var current)) return;

            _phases[_currentPhaseNumber] = current with
            {
                Status = status,
                Message = message ?? current.Message
            };
            RenderAllPhases();
        }
    }

    public void Finalize()
    {
        lock (_lock)
        {
            if (!_isInitialized || _isFinalized) return;

            _isFinalized = true;
            RenderAllPhases();
            Console.WriteLine(); // End progress section
        }
    }

    private void RenderAllPhases()
    {
        // Clear previous output using toolbox
        ProgressToolbox.ClearPreviousLines(_lastRenderedLineCount);

        // Render each phase in order - explicit use of ProgressLineRenderer
        var lines = _phases.Values
            .OrderBy(p => p.PhaseNumber)
            .Select(ProgressLineRenderer.Render)
            .ToList();

        foreach (var line in lines)
        {
            Console.WriteLine(line);
        }

        _lastRenderedLineCount = lines.Count;
    }
}
```

**Step 3: Verify build**

Run: `dotnet build src/PerformanceTester.Cli`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/PerformanceTester.Cli/Output/ProgressReporter.cs
git commit -m "$(cat <<'EOF'
feat(cli): rewrite ProgressReporter for multi-phase display

Replaces simple spinner with:
- Multi-line phase progress display
- Progress bars with Unicode block characters
- Status emojis for completion states
- Thread-safe updates via locking
- ANSI escape sequences for line overwriting

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Create PhaseInfoConverter Static Class and Progress Adapter

**Files:**
- Create: `src/PerformanceTester.Cli/Output/PhaseInfoConverter.cs`
- Create: `src/PerformanceTester.Cli/Output/OrchestratorProgressAdapter.cs`

**Step 1: Create PhaseInfoConverter static class**

Following CODING_GUIDELINES.md: Static class for pure conversion logic (no state needed).

```csharp
using PerformanceTester.Orchestration;

namespace PerformanceTester.Cli.Output;

/// <summary>
/// Converts orchestrator PhaseInfo to TestProgress.
/// Static class - pure conversion functions (CODING_GUIDELINES: Static Classes for Pure Logic).
/// </summary>
internal static class PhaseInfoConverter
{
    /// <summary>
    /// Converts PhaseState to PhaseStatus.
    /// </summary>
    public static PhaseStatus ConvertState(PhaseState state) => state switch
    {
        PhaseState.Starting => PhaseStatus.InProgress,
        PhaseState.Completed => PhaseStatus.Completed,
        PhaseState.Failed => PhaseStatus.Failed,
        _ => PhaseStatus.Pending
    };

    /// <summary>
    /// Creates TestProgress for Setup phase.
    /// Explicit parameters - no hidden state (CODING_GUIDELINES: Explicit Parameters).
    /// </summary>
    public static TestProgress CreateSetupProgress(PhaseStatus status, string? message)
        => new("Setup", phaseNumber: 1, totalPhases: 4, status, Message: message);

    /// <summary>
    /// Creates TestProgress for Warmup phase.
    /// </summary>
    public static TestProgress CreateWarmupProgress(PhaseStatus status, string? message)
        => new("Warmup", phaseNumber: 2, totalPhases: 4, status, Message: message);

    /// <summary>
    /// Creates TestProgress for Event Processing phase.
    /// All values explicit at call site.
    /// </summary>
    public static TestProgress CreateEventProgress(
        PhaseStatus status,
        int currentEvents,
        int totalEvents,
        string? message = null)
        => new("Event Processing", phaseNumber: 3, totalPhases: 4, status,
            current: currentEvents, total: totalEvents, unit: "events", Message: message);

    /// <summary>
    /// Creates TestProgress for API Load Testing phase.
    /// All values explicit at call site.
    /// </summary>
    public static TestProgress CreateApiProgress(
        PhaseStatus status,
        double elapsedSeconds,
        double totalSeconds,
        int requestCount,
        string? message = null)
        => new("API Load Testing", phaseNumber: 4, totalPhases: 4, status,
            current: elapsedSeconds, total: totalSeconds, unit: "s",
            Message: message ?? $"{requestCount} req");
}
```

**Step 2: Create OrchestratorProgressAdapter**

Instance class because it tracks state (event count, API start time).

```csharp
using PerformanceTester.Orchestration;

namespace PerformanceTester.Cli.Output;

/// <summary>
/// Adapts orchestrator PhaseInfo to TestProgress for the progress reporter.
/// Instance class - has mutable tracking state (CODING_GUIDELINES: Static Classes for Pure Logic - this has state).
/// Uses PhaseInfoConverter for pure conversions (CODING_GUIDELINES: Toolbox Pattern).
/// </summary>
public sealed class OrchestratorProgressAdapter : IProgress<PhaseInfo>
{
    private readonly IProgressReporter _progressReporter;
    private readonly int _totalEventCount;
    private readonly TimeSpan _apiDuration;

    // Mutable tracking state
    private int _currentEventCount;
    private DateTime _apiStartTime;
    private int _apiRequestCount;

    public OrchestratorProgressAdapter(
        IProgressReporter progressReporter,
        int totalEventCount,
        TimeSpan apiDuration)
    {
        _progressReporter = progressReporter ?? throw new ArgumentNullException(nameof(progressReporter));
        _totalEventCount = totalEventCount;
        _apiDuration = apiDuration;
    }

    public void Report(PhaseInfo value)
    {
        var status = PhaseInfoConverter.ConvertState(value.State);

        // Convert to TestProgress - explicit switch, no hidden logic
        var progress = value.Phase switch
        {
            TestPhase.Setup => PhaseInfoConverter.CreateSetupProgress(status, value.Message),

            TestPhase.Warmup => PhaseInfoConverter.CreateWarmupProgress(status, value.Message),

            TestPhase.EventTest => PhaseInfoConverter.CreateEventProgress(
                status,
                currentEvents: status == PhaseStatus.Completed ? _totalEventCount : _currentEventCount,
                totalEvents: _totalEventCount,
                message: value.Message),

            TestPhase.ApiTest => PhaseInfoConverter.CreateApiProgress(
                status,
                elapsedSeconds: status == PhaseStatus.Completed
                    ? _apiDuration.TotalSeconds
                    : (DateTime.UtcNow - _apiStartTime).TotalSeconds,
                totalSeconds: _apiDuration.TotalSeconds,
                requestCount: _apiRequestCount,
                message: value.Message),

            _ => new TestProgress("Unknown", 0, 4, status, Message: value.Message)
        };

        _progressReporter.ReportProgress(progress);
    }

    /// <summary>
    /// Updates event count. Called from consumer progress callback.
    /// </summary>
    public void UpdateEventCount(int count)
    {
        _currentEventCount = count;
        _progressReporter.ReportProgress(PhaseInfoConverter.CreateEventProgress(
            PhaseStatus.InProgress,
            currentEvents: count,
            totalEvents: _totalEventCount));
    }

    /// <summary>
    /// Starts API tracking. Called when API test phase begins.
    /// </summary>
    public void StartApiTracking()
    {
        _apiStartTime = DateTime.UtcNow;
        _apiRequestCount = 0;
    }

    /// <summary>
    /// Updates API progress. Called from k6 progress callback.
    /// </summary>
    public void UpdateApiProgress(int requestCount)
    {
        _apiRequestCount = requestCount;
        var elapsed = (DateTime.UtcNow - _apiStartTime).TotalSeconds;
        _progressReporter.ReportProgress(PhaseInfoConverter.CreateApiProgress(
            PhaseStatus.InProgress,
            elapsedSeconds: elapsed,
            totalSeconds: _apiDuration.TotalSeconds,
            requestCount: requestCount));
    }
}
```

**Step 2: Verify build**

Run: `dotnet build src/PerformanceTester.Cli`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/PerformanceTester.Cli/Output/PhaseInfoConverter.cs
git add src/PerformanceTester.Cli/Output/OrchestratorProgressAdapter.cs
git commit -m "$(cat <<'EOF'
feat(cli): add PhaseInfoConverter and OrchestratorProgressAdapter

- PhaseInfoConverter: static class with pure conversion functions
  - ConvertState, CreateSetupProgress, CreateWarmupProgress
  - CreateEventProgress, CreateApiProgress
- OrchestratorProgressAdapter: instance class for tracking progress
  - Tracks event count and API start time
  - Uses PhaseInfoConverter for conversions (toolbox pattern)

Follows CODING_GUIDELINES: static class for pure logic, instance for state.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Update TestCommand to Wire Progress Reporting

**Files:**
- Modify: `src/PerformanceTester.Cli/Commands/TestCommand.cs`

**Step 1: Read current file**

Read the current `TestCommand.cs` to understand the execution flow.

**Step 2: Update ExecuteAsync to use progress reporting**

Following CODING_GUIDELINES.md: Explicit construction, all parameters visible at call site.

Find the section that calls `orchestrator.RunTestAsync` and update it:

```csharp
// In ExecuteAsync method, replace the progressReporter.Start()/Stop() pattern with:

consoleWriter.WriteHeader("Running Performance Test");
progressReporter.Initialize();

// Create progress adapter - all parameters explicit at call site
var progressAdapter = new OrchestratorProgressAdapter(
    progressReporter: progressReporter,
    totalEventCount: config.EventCount,
    apiDuration: config.ApiDurationOrDefault);

try
{
    var report = await orchestrator.RunTestAsync(
        configuration: config,
        progress: progressAdapter,
        cancellationToken: cancellationToken);

    progressReporter.Finalize();

    // ... rest of success handling (display results)
}
catch (OperationCanceledException)
{
    progressReporter.SetPhaseStatus(PhaseStatus.Cancelled);
    progressReporter.Finalize();
    consoleWriter.WriteWarning("Test cancelled by user");
    return 130;
}
catch (TimeoutException ex)
{
    progressReporter.SetPhaseStatus(PhaseStatus.Failed, message: ex.Message);
    progressReporter.Finalize();
    consoleWriter.WriteError($"Timeout: {ex.Message}");
    logger.LogError(ex, "Test failed with timeout");
    return 2;
}
catch (InvalidOperationException ex)
{
    progressReporter.SetPhaseStatus(PhaseStatus.Failed, message: ex.Message);
    progressReporter.Finalize();
    consoleWriter.WriteError($"Test failed: {ex.Message}");
    logger.LogError(ex, "Test failed with invalid operation");
    return 3;
}
catch (Exception ex)
{
    progressReporter.SetPhaseStatus(PhaseStatus.Failed, message: ex.Message);
    progressReporter.Finalize();
    consoleWriter.WriteError($"Unexpected error: {ex.Message}");
    logger.LogError(ex, "Test failed with unexpected error");
    return 1;
}
```

**Step 3: Add using statement**

Add at top of file:
```csharp
using PerformanceTester.Cli.Output;
```

**Step 4: Remove old Start/Stop methods**

Remove `progressReporter.Start()` and `progressReporter.Stop()` calls - replaced with `Initialize()`, `Finalize()`, and `SetPhaseStatus()`.

**Step 5: Verify build**

Run: `dotnet build src/PerformanceTester.Cli`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add src/PerformanceTester.Cli/Commands/TestCommand.cs
git commit -m "$(cat <<'EOF'
feat(cli): wire progress reporting in TestCommand

- Creates OrchestratorProgressAdapter with explicit parameters
- Passes progress adapter to orchestrator.RunTestAsync
- Uses Initialize/Finalize/SetPhaseStatus for lifecycle
- Shows cancel/fail emojis on errors

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Add Real-Time Event Progress Updates

**Files:**
- Modify: `src/PerformanceTester.Orchestration/TestOrchestrator.cs`

**Step 1: Read current orchestrator implementation**

Review how `ExecuteEventTestPhaseAsync` works and where event counts are available.

**Step 2: Add consumer progress reporting**

Following CODING_GUIDELINES.md: Explicit callback, no hidden behavior.

Update `ExecuteEventTestPhaseAsync` to create consumer progress callback:

```csharp
// In ExecuteEventTestPhaseAsync, create explicit progress callback
// (CODING_GUIDELINES: Explicit Parameters - callback logic visible here)
IProgress<ConsumerPhaseInfo>? consumerProgress = null;
if (progress != null)
{
    consumerProgress = new Progress<ConsumerPhaseInfo>(info =>
    {
        // Only report on EventReceived with valid count
        if (info.Phase == ConsumerPhase.EventReceived && info.EventCount.HasValue)
        {
            // Report via PhaseInfo - adapter converts to TestProgress
            progress.Report(PhaseInfo.Starting(
                TestPhase.EventTest,
                $"Processing: {info.EventCount}/{config.EventCount} events"));
        }
    });
}

// Pass to consumer - explicit parameter
var consumerTask = _eventConsumer.StartTrackingEventsAsync(
    expectedCount: config.EventCount,
    inactivityTimeout: config.InactivityTimeoutOrDefault,
    progress: consumerProgress,
    cancellationToken: cancellationToken);
```

**Step 3: Verify build**

Run: `dotnet build src/PerformanceTester.Orchestration`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/PerformanceTester.Orchestration/TestOrchestrator.cs
git commit -m "$(cat <<'EOF'
feat(orchestration): add real-time event progress reporting

Creates explicit consumer progress callback in ExecuteEventTestPhaseAsync.
Reports event count updates through main progress reporter.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: Add Real-Time API Progress Updates

**Files:**
- Create: `src/PerformanceTester.ApiLoadTesting/ApiLoadProgress.cs`
- Modify: `src/PerformanceTester.ApiLoadTesting/IApiLoadTester.cs`
- Modify: `src/PerformanceTester.ApiLoadTesting/ApiLoadTestService.cs`
- Modify: `src/PerformanceTester.ApiLoadTesting/K6Executor.cs`

**Step 1: Create ApiLoadProgress record**

Following CODING_GUIDELINES.md: Plain data record, no factory methods.

Create `src/PerformanceTester.ApiLoadTesting/ApiLoadProgress.cs`:

```csharp
namespace PerformanceTester.ApiLoadTesting;

/// <summary>
/// Progress information for API load testing.
/// Plain data record (CODING_GUIDELINES: Explicit Over Implicit).
/// </summary>
public readonly record struct ApiLoadProgress(
    double ElapsedSeconds,
    double TotalSeconds,
    int RequestCount,
    int SuccessCount,
    int FailedCount);
```

**Step 2: Update IApiLoadTester interface**

Add progress parameter - explicit at call site:

```csharp
Task<ApiLoadTestResult> StartTestAsync(
    string targetUrl,
    TimeSpan duration,
    int virtualUsers,
    int maxConsecutiveFailures = 3,
    string? scriptDirectory = null,
    IProgress<ApiLoadProgress>? progress = null,  // Explicit optional parameter
    CancellationToken cancellationToken = default);
```

**Step 3: Update ApiLoadTestService**

Pass progress to K6Executor. Report progress during metric parsing loop.

**Step 4: Update K6Executor to report progress**

In the metric parsing loop, report progress periodically (e.g., every 100 metrics or every second):

```csharp
// In ExecuteAsync, during metric parsing loop:
var lastProgressReport = DateTime.UtcNow;
var requestCount = 0;
var successCount = 0;
var failedCount = 0;

await foreach (var line in process.StandardOutput.ReadLinesAsync(cancellationToken))
{
    var metric = _metricsParser.ParseLine(line);
    if (metric != null)
    {
        metrics.Add(metric);

        // Update counts from metric
        if (metric.Type == "http_req_duration")
        {
            requestCount++;
            // Success/fail determined by http_req_failed metric
        }

        // Report progress every 500ms (avoid flooding)
        if ((DateTime.UtcNow - lastProgressReport).TotalMilliseconds >= 500)
        {
            var elapsed = (DateTime.UtcNow - testStartTime).TotalSeconds;
            progress?.Report(new ApiLoadProgress(
                ElapsedSeconds: elapsed,
                TotalSeconds: totalDuration.TotalSeconds,
                RequestCount: requestCount,
                SuccessCount: successCount,
                FailedCount: failedCount));
            lastProgressReport = DateTime.UtcNow;
        }
    }
}
```

**Step 5: Verify build**

Run: `dotnet build src/PerformanceTester.ApiLoadTesting`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add src/PerformanceTester.ApiLoadTesting/
git commit -m "$(cat <<'EOF'
feat(api-load-testing): add real-time progress reporting

- Add ApiLoadProgress record (plain data, no factory methods)
- Add IProgress<ApiLoadProgress> parameter to StartTestAsync
- Report progress every 500ms during k6 metric parsing

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 10: Wire API Progress to Orchestrator

**Files:**
- Modify: `src/PerformanceTester.Orchestration/TestOrchestrator.cs`

**Step 1: Update ExecuteApiTestPhaseAsync**

Following CODING_GUIDELINES.md: Explicit callback, all values visible.

```csharp
// In ExecuteApiTestPhaseAsync, create explicit API progress callback:
IProgress<ApiLoadProgress>? apiProgress = null;
if (progress != null)
{
    apiProgress = new Progress<ApiLoadProgress>(info =>
    {
        // Report via PhaseInfo - explicit message format
        progress.Report(PhaseInfo.Starting(
            TestPhase.ApiTest,
            $"API: {info.ElapsedSeconds:F1}s/{info.TotalSeconds:F1}s ({info.RequestCount} req)"));
    });
}

// Pass to StartTestAsync - explicit parameter
var result = await _apiLoadTester.StartTestAsync(
    targetUrl: config.ApiUrl,
    duration: config.ApiDurationOrDefault,
    virtualUsers: config.ApiWorkers,
    maxConsecutiveFailures: config.MaxConsecutiveApiFailures,
    scriptDirectory: config.ResultsFolder,
    progress: apiProgress,
    cancellationToken: cancellationToken);
```

**Step 2: Verify build**

Run: `dotnet build src/PerformanceTester.Orchestration`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/PerformanceTester.Orchestration/TestOrchestrator.cs
git commit -m "$(cat <<'EOF'
feat(orchestration): wire API progress to main progress reporter

Creates explicit API progress callback in ExecuteApiTestPhaseAsync.
Reports elapsed time and request count with explicit message format.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 11: Add ProgressMessageParser Static Class

**Files:**
- Create: `src/PerformanceTester.Cli/Output/ProgressMessageParser.cs`
- Modify: `src/PerformanceTester.Cli/Output/OrchestratorProgressAdapter.cs`

Following CODING_GUIDELINES.md: Static class for pure parsing functions (no state).
Avoids embedding regex in instance class.

**Step 1: Create ProgressMessageParser**

```csharp
using System.Text.RegularExpressions;

namespace PerformanceTester.Cli.Output;

/// <summary>
/// Parses progress messages from orchestrator.
/// Static class - pure parsing functions (CODING_GUIDELINES: Static Classes for Pure Logic).
/// </summary>
internal static partial class ProgressMessageParser
{
    /// <summary>
    /// Tries to parse event progress from message like "Processing: 5000/10000 events".
    /// Returns null if parsing fails.
    /// </summary>
    public static (int Current, int Total)? TryParseEventProgress(string? message)
    {
        if (string.IsNullOrEmpty(message)) return null;

        var match = EventProgressRegex().Match(message);
        if (!match.Success) return null;

        return (
            Current: int.Parse(match.Groups[1].Value),
            Total: int.Parse(match.Groups[2].Value));
    }

    /// <summary>
    /// Tries to parse API progress from message like "API: 15.2s/30.0s (1523 req)".
    /// Returns null if parsing fails.
    /// </summary>
    public static (double Elapsed, double Total, int Requests)? TryParseApiProgress(string? message)
    {
        if (string.IsNullOrEmpty(message)) return null;

        var match = ApiProgressRegex().Match(message);
        if (!match.Success) return null;

        return (
            Elapsed: double.Parse(match.Groups[1].Value),
            Total: double.Parse(match.Groups[2].Value),
            Requests: int.Parse(match.Groups[3].Value));
    }

    [GeneratedRegex(@"Processing:\s*(\d+)/(\d+)\s*events")]
    private static partial Regex EventProgressRegex();

    [GeneratedRegex(@"API:\s*([\d.]+)s/([\d.]+)s\s*\((\d+)\s*req\)")]
    private static partial Regex ApiProgressRegex();
}
```

**Step 2: Update OrchestratorProgressAdapter to use parser**

Update the Report method to use ProgressMessageParser:

```csharp
public void Report(PhaseInfo value)
{
    var status = PhaseInfoConverter.ConvertState(value.State);

    var progress = value.Phase switch
    {
        TestPhase.Setup => PhaseInfoConverter.CreateSetupProgress(status, value.Message),

        TestPhase.Warmup => PhaseInfoConverter.CreateWarmupProgress(status, value.Message),

        TestPhase.EventTest => CreateEventProgressFromMessage(status, value.Message),

        TestPhase.ApiTest => CreateApiProgressFromMessage(status, value.Message),

        _ => new TestProgress("Unknown", 0, 4, status, Message: value.Message)
    };

    _progressReporter.ReportProgress(progress);
}

private TestProgress CreateEventProgressFromMessage(PhaseStatus status, string? message)
{
    // Try to parse real-time progress from message
    if (status == PhaseStatus.InProgress)
    {
        var parsed = ProgressMessageParser.TryParseEventProgress(message);
        if (parsed.HasValue)
        {
            _currentEventCount = parsed.Value.Current;
            return PhaseInfoConverter.CreateEventProgress(
                status,
                currentEvents: parsed.Value.Current,
                totalEvents: parsed.Value.Total);
        }
    }

    // Fallback to stored values
    return PhaseInfoConverter.CreateEventProgress(
        status,
        currentEvents: status == PhaseStatus.Completed ? _totalEventCount : _currentEventCount,
        totalEvents: _totalEventCount,
        message: message);
}

private TestProgress CreateApiProgressFromMessage(PhaseStatus status, string? message)
{
    // Try to parse real-time progress from message
    if (status == PhaseStatus.InProgress)
    {
        var parsed = ProgressMessageParser.TryParseApiProgress(message);
        if (parsed.HasValue)
        {
            _apiRequestCount = parsed.Value.Requests;
            return PhaseInfoConverter.CreateApiProgress(
                status,
                elapsedSeconds: parsed.Value.Elapsed,
                totalSeconds: parsed.Value.Total,
                requestCount: parsed.Value.Requests);
        }
    }

    // Fallback to stored values
    return PhaseInfoConverter.CreateApiProgress(
        status,
        elapsedSeconds: status == PhaseStatus.Completed
            ? _apiDuration.TotalSeconds
            : (DateTime.UtcNow - _apiStartTime).TotalSeconds,
        totalSeconds: _apiDuration.TotalSeconds,
        requestCount: _apiRequestCount,
        message: message);
}
```

**Step 3: Verify build**

Run: `dotnet build src/PerformanceTester.Cli`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/PerformanceTester.Cli/Output/ProgressMessageParser.cs
git add src/PerformanceTester.Cli/Output/OrchestratorProgressAdapter.cs
git commit -m "$(cat <<'EOF'
feat(cli): add ProgressMessageParser for message parsing

- Add static ProgressMessageParser with generated regex
- Update OrchestratorProgressAdapter to use parser
- Follows CODING_GUIDELINES: static class for pure functions

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 12: Integration Test - Manual Verification

**Files:**
- None (manual testing)

**Step 1: Build the solution**

Run: `dotnet build`
Expected: Build succeeded with no warnings

**Step 2: Start infrastructure**

```bash
docker-compose -f /workspace/scripts/infrastructure/docker-compose.yml up -d
```

**Step 3: Start a test service**

```bash
cd /workspace/implementations/go
./goReferenceService &
```

**Step 4: Run performance test with new progress display**

```bash
cd /workspace/performance-tester-dotnet
dotnet run --project src/PerformanceTester.Cli -- test --port 8094 --events 1000 --api-duration 10s
```

**Step 5: Verify expected output**

Expected output:
```
=== Running Performance Test ===

  [1/4] Setup             ✓ Complete
  [2/4] Warmup            ✓ Complete
  [3/4] Event Processing  ████████████████████  1,000/1,000 events  ✓ Complete
  [4/4] API Load Testing  ████████████████████  10.0/10.0s (523 req)  ✓ Complete

=== Test Results ===
...
```

**Step 6: Test cancellation**

Run test and press Ctrl+C during Event Processing:
- Expected: Current phase shows `⊘ Cancelled`

**Step 7: Test failure**

Start test without service running:
- Expected: Setup phase shows `✗ Failed: Service not found...`

---

### Task 13: Remove Obsolete Start/Stop Methods

**Files:**
- Modify: `src/PerformanceTester.Cli/Output/IProgressReporter.cs` (if needed)

**Step 1: Verify old methods removed**

Run: `grep -r "\.Start()\|\.Stop()" src/PerformanceTester.Cli/`
Expected: No matches for progressReporter.Start()/Stop()

**Step 2: Commit cleanup (if needed)**

```bash
git add src/PerformanceTester.Cli/
git commit -m "$(cat <<'EOF'
refactor(cli): remove obsolete Start/Stop from IProgressReporter

Replaced by Initialize/Finalize/SetPhaseStatus methods.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 14: Final Build and Test

**Step 1: Clean and rebuild**

```bash
cd /workspace/performance-tester-dotnet
dotnet clean
dotnet build
```

Expected: Build succeeded with 0 warnings

**Step 2: Run existing tests**

```bash
dotnet test
```

Expected: All tests pass

**Step 3: Final commit**

```bash
git add .
git commit -m "$(cat <<'EOF'
feat(cli): complete TUI progress display implementation

Adds multi-phase progress bars with real-time updates and status emojis.
Follows CODING_GUIDELINES.md: static classes for pure logic, explicit params.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
EOF
)"
```

---

## Summary

This plan implements a multi-phase progress display following CODING_GUIDELINES.md principles:

### New Static Classes (Pure Functions)
- `ProgressToolbox` - Progress bar rendering, emoji lookup, ANSI cursor ops
- `ProgressLineRenderer` - Complete line rendering for a phase
- `PhaseInfoConverter` - PhaseInfo → TestProgress conversion
- `ProgressMessageParser` - Regex parsing for progress messages

### New Instance Classes (With State)
- `ProgressReporter` - Tracks phases, coordinates rendering
- `OrchestratorProgressAdapter` - Tracks event/API counts, converts progress

### New Data Records (Plain Data)
- `TestProgress` - Progress data for a phase
- `ApiLoadProgress` - API test progress data
- `PhaseStatus` - Enum for completion states

### Modified Files
- `IProgressReporter` - Simplified interface (Initialize/Finalize/ReportProgress/SetPhaseStatus)
- `TestCommand` - Wires progress adapter to orchestrator
- `TestOrchestrator` - Creates progress callbacks for consumer and API
- `IApiLoadTester` / `ApiLoadTestService` / `K6Executor` - Progress parameter

### Key Design Decisions (from CODING_GUIDELINES.md)
1. **Static classes for pure logic** - All rendering/parsing is stateless
2. **Explicit parameters** - No hidden configuration objects
3. **Toolbox pattern** - Small, single-purpose functions in shared toolbox
4. **Instance classes only when needed** - Only for mutable state tracking
5. **Composition over interfaces** - Interface only for DI (IProgressReporter)
