using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;
using static PerformanceTester.Functional.Result<PerformanceTester.Orchestration.ValueObjects.WorkerCount, string>;

namespace PerformanceTester.Orchestration.ValueObjects;

/// <summary>
/// Value object representing the number of concurrent API workers (>= 1).
/// </summary>
public sealed record WorkerCount : NonNegativeInt
{
    private WorkerCount(int value) : base(value) { }

    public static Result<WorkerCount, string> Create(int value) => value >= 1
            ? new Success(new WorkerCount(value))
            : new Failure($"API workers must be at least 1 (got: {value})");
}
