using System.Text.RegularExpressions;
using JoanComasFdz.Result;

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
    /// <returns>Result containing parsed TimeSpan or a DurationParseError.</returns>
    public static Result<TimeSpan, DurationParseError> Parse(string duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return new Result<TimeSpan, DurationParseError>.Failure(new DurationParseError.Empty());

        var match = DurationPattern().Match(duration.Trim().ToLowerInvariant());
        if (!match.Success)
            return new Result<TimeSpan, DurationParseError>.Failure(new DurationParseError.InvalidFormat(duration));

        var value = int.Parse(match.Groups[1].Value);
        var unit = match.Groups[2].Value;

        return unit switch
        {
            "s" => new Result<TimeSpan, DurationParseError>.Success(TimeSpan.FromSeconds(value)),
            "m" => new Result<TimeSpan, DurationParseError>.Success(TimeSpan.FromMinutes(value)),
            "h" => new Result<TimeSpan, DurationParseError>.Success(TimeSpan.FromHours(value)),
            _ => new Result<TimeSpan, DurationParseError>.Failure(new DurationParseError.UnknownUnit(unit[0]))
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
        if (Parse(duration) is Result<TimeSpan, DurationParseError>.Success(var value))
        {
            result = value;
            return true;
        }

        result = TimeSpan.Zero;
        return false;
    }

    /// <summary>
    /// Validates a duration string format.
    /// </summary>
    /// <param name="duration">Duration string to validate</param>
    /// <returns>True if valid format</returns>
    public static bool IsValid(string duration) =>
        Parse(duration) is Result<TimeSpan, DurationParseError>.Success;
}
