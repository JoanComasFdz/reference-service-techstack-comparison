using System.Text.RegularExpressions;
using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Orchestration.ValueObjects.ApiDuration, string>;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed partial record ApiDuration
{
    public TimeSpan Value { get; }
    private ApiDuration(TimeSpan value) => Value = value;

    [GeneratedRegex(@"^(\d+)([smh])$", RegexOptions.Compiled)]
    private static partial Regex DurationPattern();

    public static Result<ApiDuration, string> Create(string duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return new Failure(
                $"Invalid API duration format: '{duration}'. Expected format: <number><unit> (e.g., 30s, 5m, 2h)");

        var match = DurationPattern().Match(duration.Trim().ToLowerInvariant());
        if (!match.Success)
            return new Failure(
                $"Invalid API duration format: '{duration}'. Expected format: <number><unit> (e.g., 30s, 5m, 2h)");

        var value = int.Parse(match.Groups[1].Value);
        var unit = match.Groups[2].Value;

        return unit switch
        {
            "s" => new Success(new ApiDuration(TimeSpan.FromSeconds(value))),
            "m" => new Success(new ApiDuration(TimeSpan.FromMinutes(value))),
            "h" => new Success(new ApiDuration(TimeSpan.FromHours(value))),
            _ => new Failure(
                $"Invalid API duration format: '{duration}'. Expected format: <number><unit> (e.g., 30s, 5m, 2h)")
        };
    }

    public static ApiDuration FromTimeSpan(TimeSpan value) => new(value);

    public override string ToString() => Value.ToString();
}
