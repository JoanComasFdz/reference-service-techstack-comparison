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
    /// Completes the progress display. Ensures proper line termination.
    /// </summary>
    void Complete();
}
