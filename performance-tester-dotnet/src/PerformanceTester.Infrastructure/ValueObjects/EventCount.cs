using PerformanceTester.Functional;
using static PerformanceTester.Functional.Result<PerformanceTester.Infrastructure.ValueObjects.EventCount, string>;

namespace PerformanceTester.Infrastructure.ValueObjects;

/// <summary>
/// Value object representing a non-negative count of events.
/// Base type for all event count variants (e.g. WarmupEventsCount).
/// </summary>
public record EventCount
{
    /// <summary>The validated event count.</summary>
    public int Value { get; }

    /// <summary>Constructor for derived records.</summary>
    protected EventCount(int value) => Value = value;

    /// <summary>
    /// Creates an <see cref="EventCount"/> from a raw integer.
    /// Returns Failure if the value is negative.
    /// </summary>
    public static Result<EventCount, string> Create(int value) => value >= 0
            ? new Success(new EventCount(value))
            : new Failure($"Event count cannot be negative (got: {value})");

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
