using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Orchestration.ValueObjects.Port, string>;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record Port
{
    public int Value { get; }
    private Port(int value) => Value = value;

    public static Result<Port, string> Create(int value) =>
        value is >= 1 and <= 65535
            ? new Success(new Port(value))
            : new Failure($"Port must be between 1 and 65535 (got: {value})");

    public static Port FromInt(int value) => new(value);

    public override string ToString() => Value.ToString();
}
