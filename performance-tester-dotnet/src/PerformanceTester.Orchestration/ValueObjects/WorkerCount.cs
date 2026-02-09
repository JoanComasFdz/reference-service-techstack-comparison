using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Orchestration.ValueObjects.WorkerCount, string>;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record WorkerCount
{
    public ushort Value { get; }
    private WorkerCount(ushort value) => Value = value;

    public static Result<WorkerCount, string> Create(int value) =>
        value is >= 1 and <= 1000
            ? new Success(new WorkerCount((ushort)value))
            : new Failure($"API workers must be between 1 and 1,000 (got: {value})");

    public override string ToString() => Value.ToString();
}
