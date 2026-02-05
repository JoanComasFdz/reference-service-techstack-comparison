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
    private bool _isCompleted;

    public void Initialize()
    {
        lock (_lock)
        {
            if (_isInitialized) return;

            _phases.Clear();
            _currentPhaseNumber = 0;
            _lastRenderedLineCount = 0;
            _isInitialized = true;
            _isCompleted = false;

            Console.WriteLine(); // Start progress section
        }
    }

    public void ReportProgress(TestProgress progress)
    {
        lock (_lock)
        {
            if (!_isInitialized || _isCompleted) return;

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
            if (!_isInitialized || _isCompleted) return;
            if (!_phases.TryGetValue(_currentPhaseNumber, out var current)) return;

            _phases[_currentPhaseNumber] = current with
            {
                Status = status,
                Message = message ?? current.Message
            };
            RenderAllPhases();
        }
    }

    public void Complete()
    {
        lock (_lock)
        {
            if (!_isInitialized || _isCompleted) return;

            _isCompleted = true;
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
