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

    /// <summary>
    /// Renders a simple progress line without phase number prefix.
    /// Used for displaying only the current active progress.
    /// </summary>
    public static string RenderSimple(TestProgress progress)
    {
        var sb = new StringBuilder();

        // Phase name with indent
        sb.Append($"  {progress.PhaseName}: ");

        if (progress.Status == PhaseStatus.InProgress && progress.Total > 0)
        {
            // Progress bar using toolbox
            var percent = ProgressToolbox.CalculatePercent(progress.Current, progress.Total);
            sb.Append(ProgressToolbox.RenderProgressBar(percent));
            sb.Append("  ");

            // Progress values - pattern matching for unit type
            sb.Append(progress.Unit switch
            {
                "events" => ProgressToolbox.FormatEventProgress((int)progress.Current, (int)progress.Total),
                "s" => ProgressToolbox.FormatTimeProgress(progress.Current, progress.Total, progress.Message),
                _ => $"{progress.Current:F0}/{progress.Total:F0}"
            });
        }

        return sb.ToString();
    }

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
