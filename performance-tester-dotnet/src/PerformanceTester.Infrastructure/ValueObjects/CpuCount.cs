using PerformanceTester.Functional;
using static PerformanceTester.Functional.Result<PerformanceTester.Infrastructure.ValueObjects.CpuCount, string>;

namespace PerformanceTester.Infrastructure.ValueObjects;

/// <summary>
/// Number of logical CPU cores. Must be at least 1.
/// </summary>
public sealed record CpuCount : NonNegativeInt
{
    private CpuCount(int value) : base(value) { }

    /// <summary>
    /// Creates a <see cref="CpuCount"/> from a raw integer.
    /// Returns Failure if the value is less than 1.
    /// </summary>
    public static Result<CpuCount, string> Create(int value) => value >= 1
        ? new Success(new CpuCount(value))
        : new Failure($"CPU count must be at least 1 (got: {value})");

    /// <summary>
    /// Creates a <see cref="CpuCount"/> from a trusted source (e.g. <c>Environment.ProcessorCount</c>).
    /// Skips validation. Use only when the value is guaranteed valid.
    /// </summary>
    public static CpuCount FromInt(int value) => new(value);
}
