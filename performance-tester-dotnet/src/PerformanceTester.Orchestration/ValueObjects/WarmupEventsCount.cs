using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;
using static PerformanceTester.Functional.Result<PerformanceTester.Orchestration.ValueObjects.WarmupEventsCount, string>;

namespace PerformanceTester.Orchestration.ValueObjects;

/// <summary>
/// Event count specifically for warmup phase. Inherits from <see cref="EventCount"/>
/// so it can be passed directly wherever <see cref="EventCount"/> is expected.
/// </summary>
public sealed record WarmupEventsCount : EventCount
{
    private WarmupEventsCount(int value) : base(value) { }

    /// <summary>
    /// Creates a <see cref="WarmupEventsCount"/> from a raw integer.
    /// Returns Failure if the value is negative.
    /// </summary>
    public static new Result<WarmupEventsCount, string> Create(int value) => value >= 0
            ? new Success(new WarmupEventsCount(value))
            : new Failure($"Warmup event count cannot be negative (got: {value})");

    public static WarmupEventsCount FromInt(int value) => new(value);
}
