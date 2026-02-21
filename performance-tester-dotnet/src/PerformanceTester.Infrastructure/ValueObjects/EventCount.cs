using PerformanceTester.Functional;

namespace PerformanceTester.Infrastructure.ValueObjects;

/// <summary>
/// Value object representing a non-negative count of events.
/// Base type for all event count variants (e.g. WarmupEventsCount).
/// </summary>
public record EventCount : NonNegativeInt
{
    /// <summary>Constructor for derived records.</summary>
    protected EventCount(int value) : base(value) { }

    /// <summary>
    /// Creates an <see cref="EventCount"/> from a raw integer.
    /// Returns Failure if the value is negative.
    /// </summary>
    public static Result<EventCount, string> Create(int value) =>
        Create(value, "Event count", v => new EventCount(v));
}
