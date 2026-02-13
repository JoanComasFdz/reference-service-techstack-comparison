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
        if (string.IsNullOrEmpty(message))
        {
            return null;
        }

        var match = EventProgressRegex().Match(message);
        if (!match.Success)
        {
            return null;
        }

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
        if (string.IsNullOrEmpty(message))
        {
            return null;
        }

        var match = ApiProgressRegex().Match(message);
        if (!match.Success)
        {
            return null;
        }

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
