using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.Orchestration.ValueObjects;

/// <summary>
/// Value object representing a non-negative count of warmup API calls.
/// </summary>
public sealed record WarmupApiCallsCount : NonNegativeInt
{
    private WarmupApiCallsCount(int value) : base(value) { }

    /// <summary>
    /// Creates a <see cref="WarmupApiCallsCount"/> from a raw integer.
    /// Returns Failure if the value is negative.
    /// </summary>
    public static Result<WarmupApiCallsCount, string> Create(int value) =>
        Create(value, "Warmup API call count", v => new WarmupApiCallsCount(v));

    public static WarmupApiCallsCount FromInt(int value) => new(value);
}
