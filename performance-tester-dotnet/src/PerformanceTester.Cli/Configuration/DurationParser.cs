using System.Text.RegularExpressions;

namespace PerformanceTester.Cli.Configuration;

/// <summary>
/// Parses duration strings in k6 format (e.g., "30s", "5m", "2h").
/// </summary>
public static partial class DurationParser
{
    [GeneratedRegex(@"^(\d+)([smh])$", RegexOptions.Compiled)]
    private static partial Regex DurationPattern();

    /// <summary>
    /// Parses a duration string to TimeSpan.
    /// </summary>
    /// <param name="duration">Duration string (e.g., "30s", "5m", "2h")</param>
    /// <returns>Parsed TimeSpan</returns>
    /// <exception cref="ArgumentException">Invalid format</exception>
    public static TimeSpan Parse(string duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            throw new ArgumentException("Duration cannot be empty", nameof(duration));

        var match = DurationPattern().Match(duration.Trim().ToLowerInvariant());
        if (!match.Success)
            throw new ArgumentException(
                $"Invalid duration format: '{duration}'. Expected format: <number><unit> where unit is s (seconds), m (minutes), or h (hours). Examples: 30s, 5m, 2h",
                nameof(duration));

        var value = int.Parse(match.Groups[1].Value);
        var unit = match.Groups[2].Value;

        return unit switch
        {
            "s" => TimeSpan.FromSeconds(value),
            "m" => TimeSpan.FromMinutes(value),
            "h" => TimeSpan.FromHours(value),
            _ => throw new ArgumentException($"Unknown duration unit: {unit}", nameof(duration))
        };
    }

    /// <summary>
    /// Tries to parse a duration string to TimeSpan.
    /// </summary>
    /// <param name="duration">Duration string</param>
    /// <param name="result">Parsed TimeSpan if successful</param>
    /// <returns>True if parsing succeeded</returns>
    public static bool TryParse(string duration, out TimeSpan result)
    {
        try
        {
            result = Parse(duration);
            return true;
        }
        catch
        {
            result = TimeSpan.Zero;
            return false;
        }
    }

    /// <summary>
    /// Validates a duration string format.
    /// </summary>
    /// <param name="duration">Duration string to validate</param>
    /// <returns>True if valid format</returns>
    public static bool IsValid(string duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return false;
        return DurationPattern().IsMatch(duration.Trim().ToLowerInvariant());
    }
}
