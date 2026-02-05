namespace PerformanceTester.Cli.Output;

/// <summary>
/// Coordinates console output between logging and progress display.
/// Ensures progress bar stays at the bottom by clearing before logs and re-rendering after.
/// Static class - global coordination point (CODING_GUIDELINES: Static Classes for Pure Logic).
/// </summary>
public static class ConsoleCoordinator
{
    private static readonly object Lock = new();
    private static Action? _clearProgress;
    private static Action? _renderProgress;

    /// <summary>
    /// Registers progress display callbacks. Called by ProgressReporter on Initialize.
    /// </summary>
    public static void RegisterProgress(Action clearProgress, Action renderProgress)
    {
        lock (Lock)
        {
            _clearProgress = clearProgress;
            _renderProgress = renderProgress;
        }
    }

    /// <summary>
    /// Unregisters progress display. Called by ProgressReporter on Complete.
    /// </summary>
    public static void UnregisterProgress()
    {
        lock (Lock)
        {
            _clearProgress = null;
            _renderProgress = null;
        }
    }

    /// <summary>
    /// Writes a log line, coordinating with progress display.
    /// Clears progress, writes log, re-renders progress.
    /// </summary>
    public static void WriteLogLine(string message)
    {
        lock (Lock)
        {
            _clearProgress?.Invoke();
            Console.WriteLine(message);
            _renderProgress?.Invoke();
        }
    }
}
