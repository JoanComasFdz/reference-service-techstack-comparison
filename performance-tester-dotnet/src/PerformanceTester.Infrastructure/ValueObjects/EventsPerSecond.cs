using PerformanceTester.Functional;

namespace PerformanceTester.Infrastructure.ValueObjects;

/// <summary>
/// Value object representing a non-negative throughput rate (events per second).
/// </summary>
public sealed record EventsPerSecond : NonNegativeDouble
{
    private EventsPerSecond(double value) : base(value) { }

    /// <summary>
    /// Creates an <see cref="EventsPerSecond"/> from a raw double.
    /// Returns Failure if the value is negative.
    /// </summary>
    public static Result<EventsPerSecond, string> Create(double value) =>
        Create(value, "Events per second", v => new EventsPerSecond(v));

    /// <summary>Creates an <see cref="EventsPerSecond"/> from a raw double without validation.</summary>
    public static EventsPerSecond FromDouble(double value) => new(value);
}
