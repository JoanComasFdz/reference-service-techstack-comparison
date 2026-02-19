using PerformanceTester.Functional;

namespace PerformanceTester.Infrastructure.ValueObjects;

/// <summary>
/// Base value object for non-negative integer types (>= 0).
/// Derived types inherit validation via <see cref="Create{T}"/>.
/// </summary>
public record NonNegativeInt
{
    /// <summary>The validated non-negative value.</summary>
    public int Value { get; }

    /// <summary>Initializes a new instance with the given value.</summary>
    protected NonNegativeInt(int value) => Value = value;

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();

    /// <summary>
    /// Creates a derived <typeparamref name="T"/> from a raw integer.
    /// Returns Failure if the value is negative.
    /// </summary>
    protected static Result<T, string> Create<T>(int value, string displayName, Func<int, T> factory)
        where T : NonNegativeInt => value >= 0
            ? new Result<T, string>.Success(factory(value))
            : new Result<T, string>.Failure($"{displayName} cannot be negative (got: {value})");
}
