using Dunet;
using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Orchestration.ValueObjects.EventCount, PerformanceTester.Orchestration.ValueObjects.EventCountError>;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record EventCount
{
    public int Value { get; }
    private EventCount(int value) => Value = value;

    public static Result<EventCount, EventCountError> Create(int value) =>
        value is >= 1 and <= 1_000_000
            ? new Success(new EventCount(value))
            : new Failure(new EventCountError.OutOfRange(value));

    public override string ToString() => Value.ToString();
}

[Union]
public partial record EventCountError
{
    public partial record OutOfRange(int Value);
}
