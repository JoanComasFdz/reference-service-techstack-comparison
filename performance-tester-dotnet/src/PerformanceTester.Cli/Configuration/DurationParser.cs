using System.Text.RegularExpressions;
using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<System.TimeSpan, PerformanceTester.Cli.Configuration.DurationParseError>;

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
            return new Failure(new DurationParseError.Empty());

        var match = DurationPattern().Match(duration.Trim().ToLowerInvariant());
        if (!match.Success)
            return new Failure(new DurationParseError.InvalidFormat(duration));

        var value = int.Parse(match.Groups[1].Value);
        var unit = match.Groups[2].Value;

        return unit switch
        {
            "s" => new Success(TimeSpan.FromSeconds(value)),
            "m" => new Success(TimeSpan.FromMinutes(value)),
            "h" => new Success(TimeSpan.FromHours(value)),
            _ => new Failure(new DurationParseError.UnknownUnit(unit[0]))
        };
    }

    /// <summary>
    /// Validates a duration string format.
    /// </summary>
    /// <param name="duration">Duration string to validate</param>
    /// <returns>True if valid format</returns>
    public static bool IsValid(string duration) =>
        Parse(duration).Match(
            success: _ => true,
            failure: _ => false);
}
