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
        => new("Setup", PhaseNumber: 1, TotalPhases: 4, status, Message: message);

    /// <summary>
    /// Creates TestProgress for Warmup phase.
    /// </summary>
    public static TestProgress CreateWarmupProgress(PhaseStatus status, string? message)
        => new("Warmup", PhaseNumber: 2, TotalPhases: 4, status, Message: message);

    /// <summary>
    /// Creates TestProgress for Event Processing phase.
    /// All values explicit at call site.
    /// </summary>
    public static TestProgress CreateEventProgress(
        PhaseStatus status,
        int currentEvents,
        int totalEvents,
        string? message = null)
        => new(
            "Event Processing",
            PhaseNumber: 3,
            TotalPhases: 4,
            status,
            Current: currentEvents,
            Total: totalEvents,
            Unit: "events",
            Message: message);

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
        => new(
            "API Load Testing",
            PhaseNumber: 4,
            TotalPhases: 4,
            status,
            Current: elapsedSeconds,
            Total: totalSeconds,
            Unit: "s",
            Message: message ?? $"{requestCount} req");
}
