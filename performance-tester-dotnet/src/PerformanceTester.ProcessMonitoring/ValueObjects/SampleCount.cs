using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.ProcessMonitoring.ValueObjects;

/// <summary>
/// Value object representing a non-negative count of collected process metric samples.
/// </summary>
public sealed record SampleCount : NonNegativeInt
{
    private SampleCount(int value) : base(value) { }

    /// <summary>
    /// Creates a SampleCount from an integer. Returns Failure if negative.
    /// </summary>
    public static Result<SampleCount, string> Create(int value) => Create(value, "Sample count", v => new SampleCount(v));

    /// <summary>
    /// Creates a SampleCount from a known-valid integer (e.g., from collection.Count).
    /// </summary>
    public static SampleCount FromInt(int value) => new(value);
}
