using PerformanceTester.Functional;

namespace PerformanceTester.Infrastructure.ValueObjects;

/// <summary>
/// Base value object for non-negative double types (>= 0).
/// Derived types inherit validation via <see cref="Create{T}"/>.
/// Provides comparison and arithmetic operators; arithmetic clamps at 0.
/// </summary>
public record NonNegativeDouble : IComparable<NonNegativeDouble>
{
    /// <summary>The validated non-negative value.</summary>
    public double Value { get; }

    /// <summary>Initializes a new instance with the given value.</summary>
    protected NonNegativeDouble(double value) => Value = value;

    /// <inheritdoc/>
    public sealed override string ToString() => Value.ToString();

    /// <summary>
    /// Creates a derived <typeparamref name="T"/> from a raw double.
    /// Returns Failure if the value is negative.
    /// </summary>
    protected static Result<T, string> Create<T>(double value, string displayName, Func<double, T> factory)
        where T : NonNegativeDouble => value >= 0
            ? new Result<T, string>.Success(factory(value))
            : new Result<T, string>.Failure($"{displayName} cannot be negative (got: {value})");

    /// <inheritdoc/>
    public int CompareTo(NonNegativeDouble? other) => other is null ? 1 : Value.CompareTo(other.Value);

#pragma warning disable CS1591 // Operators are self-documenting
    // -- Same-type comparison --

    public static bool operator <(NonNegativeDouble left, NonNegativeDouble right) => left.Value < right.Value;

    public static bool operator <=(NonNegativeDouble left, NonNegativeDouble right) => left.Value <= right.Value;

    public static bool operator >(NonNegativeDouble left, NonNegativeDouble right) => left.Value > right.Value;

    public static bool operator >=(NonNegativeDouble left, NonNegativeDouble right) => left.Value >= right.Value;

    // -- Comparison with double --

    public static bool operator <(NonNegativeDouble left, double right) => left.Value < right;

    public static bool operator <(double left, NonNegativeDouble right) => left < right.Value;

    public static bool operator <=(NonNegativeDouble left, double right) => left.Value <= right;

    public static bool operator <=(double left, NonNegativeDouble right) => left <= right.Value;

    public static bool operator >(NonNegativeDouble left, double right) => left.Value > right;

    public static bool operator >(double left, NonNegativeDouble right) => left > right.Value;

    public static bool operator >=(NonNegativeDouble left, double right) => left.Value >= right;

    public static bool operator >=(double left, NonNegativeDouble right) => left >= right.Value;

    // -- Arithmetic with double (clamped at 0) --

    public static NonNegativeDouble operator +(NonNegativeDouble left, double right) => new(Math.Max(0, left.Value + right));

    public static NonNegativeDouble operator +(double left, NonNegativeDouble right) => new(Math.Max(0, left + right.Value));

    public static NonNegativeDouble operator -(NonNegativeDouble left, double right) => new(Math.Max(0, left.Value - right));

    public static NonNegativeDouble operator *(NonNegativeDouble left, double right) => new(Math.Max(0, left.Value * right));

    public static NonNegativeDouble operator *(double left, NonNegativeDouble right) => new(Math.Max(0, left * right.Value));

    public static NonNegativeDouble operator /(NonNegativeDouble left, double right) => new(Math.Max(0, left.Value / right));

    // -- Increment / Decrement (clamped at 0) --

    public static NonNegativeDouble operator ++(NonNegativeDouble value) => new(value.Value + 1);

    public static NonNegativeDouble operator --(NonNegativeDouble value) => new(Math.Max(0, value.Value - 1));
#pragma warning restore CS1591
}
