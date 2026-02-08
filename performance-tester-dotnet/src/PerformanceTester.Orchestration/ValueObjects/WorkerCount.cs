using Dunet;
using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Orchestration.ValueObjects.WorkerCount, PerformanceTester.Orchestration.ValueObjects.WorkerCountError>;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record WorkerCount
{
    public ushort Value { get; }
    private WorkerCount(ushort value) => Value = value;

    public static Result<WorkerCount, WorkerCountError> Create(int value) =>
        value is >= 1 and <= 1000
            ? new Success(new WorkerCount((ushort)value))
            : new Failure(new WorkerCountError.OutOfRange(value));

    public override string ToString() => Value.ToString();
}

[Union]
public partial record WorkerCountError
{
    public partial record OutOfRange(int Value);
}
