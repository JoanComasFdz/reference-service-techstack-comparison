using PerformanceTester.Functional;
using static PerformanceTester.Functional.Result<PerformanceTester.Infrastructure.ValueObjects.EventCount, string>;

namespace PerformanceTester.Infrastructure.ValueObjects;

/// <summary>
/// Value object representing a count of events to publish/consume (1–1,000,000).
/// </summary>
public sealed record EventCount
{
    /// <summary>The validated event count.</summary>
    public int Value { get; }

    private EventCount(int value) => Value = value;

    /// <summary>
    /// Creates an <see cref="EventCount"/> from a raw integer.
    /// Returns Failure if the value is outside the valid range (1–1,000,000).
    /// </summary>
    public static Result<EventCount, string> Create(int value) => value is >= 1 and <= 1_000_000
            ? new Success(new EventCount(value))
            : new Failure($"Events must be between 1 and 1,000,000 (got: {value})");

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
