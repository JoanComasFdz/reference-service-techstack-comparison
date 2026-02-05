namespace PerformanceTester.Cli.Output;

/// <summary>
/// Progress reporter that shows a single progress bar only during long-running operations.
/// Only displays progress for Event Processing and API Load Testing phases.
/// Instance class - has mutable state (CODING_GUIDELINES: Static Classes for Pure Logic - this has state).
/// Delegates all rendering to static classes (CODING_GUIDELINES: Toolbox Pattern).
/// </summary>
public sealed class ProgressReporter : IProgressReporter
{
    // Phases that show progress (Event Processing = 3, API Test = 4)
    private static readonly HashSet<int> ProgressPhases = [3, 4];

    private readonly object _lock = new();
    private TestProgress? _currentProgress;
    private bool _hasRenderedLine;
    private bool _isInitialized;
    private bool _isCompleted;

    public void Initialize()
    {
        lock (_lock)
        {
            if (_isInitialized) return;

            _currentProgress = null;
            _hasRenderedLine = false;
            _isInitialized = true;
            _isCompleted = false;
        }
    }

    public void ReportProgress(TestProgress progress)
    {
        lock (_lock)
        {
            if (!_isInitialized || _isCompleted) return;

            // Only show progress for Event Processing and API Test phases
            if (!ProgressPhases.Contains(progress.PhaseNumber)) return;

            // If phase completed, clear the progress line
            if (progress.Status != PhaseStatus.InProgress)
            {
                ClearCurrentLine();
                _currentProgress = null;
                return;
            }

            // Don't show progress bar at 0 - wait until there's actual progress
            // Don't show at 100% either - phase will complete and log will show it
            if (progress.Current <= 0 || (progress.Total > 0 && progress.Current >= progress.Total))
            {
                ClearCurrentLine();
                _currentProgress = null;
                return;
            }

            // Update and render current progress
            _currentProgress = progress;
            RenderCurrentProgress();
        }
    }

    public void SetPhaseStatus(PhaseStatus status, string? message = null)
    {
        lock (_lock)
        {
            if (!_isInitialized || _isCompleted) return;
            if (_currentProgress is not { } current) return;

            if (status != PhaseStatus.InProgress)
            {
                // Phase ended - clear the line
                ClearCurrentLine();
                _currentProgress = null;
            }
            else
            {
                _currentProgress = current with
                {
                    Status = status,
                    Message = message ?? current.Message
                };
                RenderCurrentProgress();
            }
        }
    }

    public void Complete()
    {
        lock (_lock)
        {
            if (!_isInitialized || _isCompleted) return;

            _isCompleted = true;
            ClearCurrentLine();
            _currentProgress = null;
        }
    }

    private void RenderCurrentProgress()
    {
        if (_currentProgress is not { } progress) return;

        // Clear previous line if we rendered one
        if (_hasRenderedLine)
        {
            ProgressToolbox.ClearPreviousLines(1);
        }

        // Render simple progress line (no phase number prefix)
        var line = ProgressLineRenderer.RenderSimple(progress);
        Console.WriteLine(line);
        _hasRenderedLine = true;
    }

    private void ClearCurrentLine()
    {
        if (_hasRenderedLine)
        {
            ProgressToolbox.ClearPreviousLines(1);
            _hasRenderedLine = false;
        }
    }
}
