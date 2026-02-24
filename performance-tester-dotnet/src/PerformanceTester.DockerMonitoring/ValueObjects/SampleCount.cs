using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.ValueObjects;

/// <summary>
/// Value object representing a non-negative count of collected metric samples.
/// </summary>
public sealed record SampleCount : NonNegativeInt
{
    private SampleCount(int value) : base(value) { }

    public static Result<SampleCount, string> Create(int value) => Create(value, "Sample count", v => new SampleCount(v));

    public static SampleCount FromInt(int value) => new(value);
}
