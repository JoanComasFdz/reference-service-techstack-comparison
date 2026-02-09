using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Orchestration.ValueObjects.WarmupApiCallsCount, string>;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record WarmupApiCallsCount
{
    public uint Value { get; }
    private WarmupApiCallsCount(uint value) => Value = value;

    public static Result<WarmupApiCallsCount, string> Create(int value) =>
        value is >= 0 and <= 1000
            ? new Success(new WarmupApiCallsCount((uint)value))
            : new Failure($"Warmup API calls must be between 0 and 1,000 (got: {value})");

    public static WarmupApiCallsCount FromUint(uint value) => new(value);

    public override string ToString() => Value.ToString();
}
