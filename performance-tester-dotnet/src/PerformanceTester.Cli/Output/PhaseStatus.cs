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
