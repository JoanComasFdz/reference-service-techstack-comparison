using System.Text.RegularExpressions;
using PerformanceTester.Functional;

namespace PerformanceTester.Orchestration.ValueObjects;

public partial record DurationValue
{
    public TimeSpan Value { get; }
    protected DurationValue(TimeSpan value) => Value = value;

    [GeneratedRegex(@"^(\d+)([smh])$", RegexOptions.Compiled)]
    private static partial Regex DurationPattern();

    protected static Result<T, string> Create<T>(string duration, string displayName, Func<TimeSpan, T> factory)
        where T : DurationValue
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            return new Result<T, string>.Failure(
                $"Invalid {displayName} format: '{duration}'. Expected format: <number><unit> (e.g., 30s, 5m, 2h)");
        }

        var match = DurationPattern().Match(duration.Trim().ToLowerInvariant());
        if (!match.Success)
        {
            return new Result<T, string>.Failure(
                $"Invalid {displayName} format: '{duration}'. Expected format: <number><unit> (e.g., 30s, 5m, 2h)");
        }

        var value = int.Parse(match.Groups[1].Value);
        var unit = match.Groups[2].Value;

        return unit switch
        {
            "s" => new Result<T, string>.Success(factory(TimeSpan.FromSeconds(value))),
            "m" => new Result<T, string>.Success(factory(TimeSpan.FromMinutes(value))),
            "h" => new Result<T, string>.Success(factory(TimeSpan.FromHours(value))),
            _ => new Result<T, string>.Failure(
                $"Invalid {displayName} format: '{duration}'. Expected format: <number><unit> (e.g., 30s, 5m, 2h)")
        };
    }

    public override string ToString() => Value.ToString();
}
