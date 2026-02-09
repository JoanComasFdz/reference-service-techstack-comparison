using System.Text.RegularExpressions;
using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Orchestration.ValueObjects.InactivityTimeout, string>;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed partial record InactivityTimeout
{
    public TimeSpan Value { get; }
    private InactivityTimeout(TimeSpan value) => Value = value;

    [GeneratedRegex(@"^(\d+)([smh])$", RegexOptions.Compiled)]
    private static partial Regex DurationPattern();

    public static Result<InactivityTimeout, string> Create(string duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return new Failure(
                $"Invalid inactivity timeout format: '{duration}'. Expected format: <number><unit> (e.g., 120s, 2m, 1h)");

        var match = DurationPattern().Match(duration.Trim().ToLowerInvariant());
        if (!match.Success)
            return new Failure(
                $"Invalid inactivity timeout format: '{duration}'. Expected format: <number><unit> (e.g., 120s, 2m, 1h)");

        var value = int.Parse(match.Groups[1].Value);
        var unit = match.Groups[2].Value;

        return unit switch
        {
            "s" => new Success(new InactivityTimeout(TimeSpan.FromSeconds(value))),
            "m" => new Success(new InactivityTimeout(TimeSpan.FromMinutes(value))),
            "h" => new Success(new InactivityTimeout(TimeSpan.FromHours(value))),
            _ => new Failure(
                $"Invalid inactivity timeout format: '{duration}'. Expected format: <number><unit> (e.g., 120s, 2m, 1h)")
        };
    }

    public static InactivityTimeout FromTimeSpan(TimeSpan value) => new(value);

    public override string ToString() => Value.ToString();
}
