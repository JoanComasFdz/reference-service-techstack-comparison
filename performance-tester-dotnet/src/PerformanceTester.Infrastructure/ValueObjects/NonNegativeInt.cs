using PerformanceTester.Functional;

namespace PerformanceTester.Infrastructure.ValueObjects;

/// <summary>
/// Base value object for non-negative integer types (>= 0).
/// Derived types inherit validation via <see cref="Create{T}"/>.
/// Provides comparison and arithmetic operators; arithmetic clamps at 0.
/// </summary>
public record NonNegativeInt : IComparable<NonNegativeInt>
{
    /// <summary>The validated non-negative value.</summary>
    public int Value { get; }

    /// <summary>Initializes a new instance with the given value.</summary>
    protected NonNegativeInt(int value) => Value = value;

    /// <inheritdoc/>
    public sealed override string ToString() => Value.ToString();

    /// <summary>
    /// Creates a derived <typeparamref name="T"/> from a raw integer.
    /// Returns Failure if the value is negative.
    /// </summary>
    protected static Result<T, string> Create<T>(int value, string displayName, Func<int, T> factory)
        where T : NonNegativeInt => value >= 0
            ? new Result<T, string>.Success(factory(value))
            : new Result<T, string>.Failure($"{displayName} cannot be negative (got: {value})");

    /// <inheritdoc/>
    public int CompareTo(NonNegativeInt? other) => other is null ? 1 : Value.CompareTo(other.Value);

#pragma warning disable CS1591 // Operators are self-documenting
    // -- Same-type comparison --

    public static bool operator <(NonNegativeInt left, NonNegativeInt right) => left.Value < right.Value;

    public static bool operator <=(NonNegativeInt left, NonNegativeInt right) => left.Value <= right.Value;

    public static bool operator >(NonNegativeInt left, NonNegativeInt right) => left.Value > right.Value;

    public static bool operator >=(NonNegativeInt left, NonNegativeInt right) => left.Value >= right.Value;

    // -- Comparison with int --

    public static bool operator <(NonNegativeInt left, int right) => left.Value < right;

    public static bool operator <(int left, NonNegativeInt right) => left < right.Value;

    public static bool operator <=(NonNegativeInt left, int right) => left.Value <= right;

    public static bool operator <=(int left, NonNegativeInt right) => left <= right.Value;

    public static bool operator >(NonNegativeInt left, int right) => left.Value > right;

    public static bool operator >(int left, NonNegativeInt right) => left > right.Value;

    public static bool operator >=(NonNegativeInt left, int right) => left.Value >= right;

    public static bool operator >=(int left, NonNegativeInt right) => left >= right.Value;

    // -- Arithmetic with int (clamped at 0) --

    public static NonNegativeInt operator +(NonNegativeInt left, int right) => new(Math.Max(0, left.Value + right));

    public static NonNegativeInt operator +(int left, NonNegativeInt right) => new(Math.Max(0, left + right.Value));

    public static NonNegativeInt operator -(NonNegativeInt left, int right) => new(Math.Max(0, left.Value - right));

    public static NonNegativeInt operator *(NonNegativeInt left, int right) => new(Math.Max(0, left.Value * right));

    public static NonNegativeInt operator *(int left, NonNegativeInt right) => new(Math.Max(0, left * right.Value));

    public static NonNegativeInt operator /(NonNegativeInt left, int right) => new(left.Value / right);

    // -- Increment / Decrement (clamped at 0) --

    public static NonNegativeInt operator ++(NonNegativeInt value) => new(value.Value + 1);

    public static NonNegativeInt operator --(NonNegativeInt value) => new(Math.Max(0, value.Value - 1));
#pragma warning restore CS1591
}
