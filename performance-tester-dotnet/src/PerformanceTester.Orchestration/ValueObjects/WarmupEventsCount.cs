using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Orchestration.ValueObjects.WarmupEventsCount, string>;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record WarmupEventsCount
{
    public int Value { get; }
    private WarmupEventsCount(int value) => Value = value;

    public static Result<WarmupEventsCount, string> Create(int value) =>
        value is >= 0 and <= 10_000
            ? new Success(new WarmupEventsCount(value))
            : new Failure($"Warmup events must be between 0 and 10,000 (got: {value})");

    public static WarmupEventsCount FromInt(int value) => new(value);

    public override string ToString() => Value.ToString();
}
