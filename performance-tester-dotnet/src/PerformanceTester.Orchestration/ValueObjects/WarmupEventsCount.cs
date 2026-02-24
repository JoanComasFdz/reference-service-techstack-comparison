using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.Orchestration.ValueObjects;

/// <summary>
/// Event count specifically for warmup phase. Inherits from <see cref="EventCount"/>
/// so it can be passed directly wherever <see cref="EventCount"/> is expected.
/// </summary>
public sealed record WarmupEventsCount : EventCount
{
    private WarmupEventsCount(int value) : base(value) { }

    public static new Result<WarmupEventsCount, string> Create(int value) =>
        Create(value, "Warmup event count", v => new WarmupEventsCount(v));

    public static WarmupEventsCount FromInt(int value) => new(value);
}
