using PerformanceTester.Functional;

namespace PerformanceTester.Infrastructure.ValueObjects;

/// <summary>
/// Base value object for non-negative double types (>= 0).
/// Derived types inherit validation via <see cref="Create{T}"/>.
/// </summary>
public record NonNegativeDouble
{
    /// <summary>The validated non-negative value.</summary>
    public double Value { get; }

    /// <summary>Initializes a new instance with the given value.</summary>
    protected NonNegativeDouble(double value) => Value = value;

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();

    /// <summary>
    /// Creates a derived <typeparamref name="T"/> from a raw double.
    /// Returns Failure if the value is negative.
    /// </summary>
    protected static Result<T, string> Create<T>(double value, string displayName, Func<double, T> factory)
        where T : NonNegativeDouble => value >= 0
            ? new Result<T, string>.Success(factory(value))
            : new Result<T, string>.Failure($"{displayName} cannot be negative (got: {value})");
}
