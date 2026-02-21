using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;
using static PerformanceTester.Functional.Result<PerformanceTester.Orchestration.ValueObjects.MaxConsecutiveFailures, string>;

namespace PerformanceTester.Orchestration.ValueObjects;

/// <summary>
/// Value object representing the maximum consecutive API failures before aborting (>= 0, where 0 = disabled).
/// </summary>
public sealed record MaxConsecutiveFailures : NonNegativeInt
{
    private MaxConsecutiveFailures(int value) : base(value) { }

    public static Result<MaxConsecutiveFailures, string> Create(int value) =>
        Create(value, "Max consecutive API failures", v => new MaxConsecutiveFailures(v));

    /// <summary>
    /// Bypass validation for trusted sources (e.g., default values).
    /// </summary>
    public static MaxConsecutiveFailures FromInt(int value) => new(value);
}
