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
