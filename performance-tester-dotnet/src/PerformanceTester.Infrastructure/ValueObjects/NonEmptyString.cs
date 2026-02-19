using PerformanceTester.Functional;

namespace PerformanceTester.Infrastructure.ValueObjects;

/// <summary>
/// Base value object for non-empty string types.
/// Derived types inherit validation and trimming via <see cref="Create{T}"/>.
/// </summary>
public record NonEmptyString
{
    /// <summary>The validated, trimmed string value.</summary>
    public string Value { get; }

    /// <summary>Initializes a new instance with the given value.</summary>
    protected NonEmptyString(string value) => Value = value;

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>
    /// Creates a derived <typeparamref name="T"/> from a raw string.
    /// Returns Failure if the value is null, empty, or whitespace.
    /// </summary>
    protected static Result<T, string> Create<T>(string value, string displayName, Func<string, T> factory)
        where T : NonEmptyString => !string.IsNullOrWhiteSpace(value)
            ? new Result<T, string>.Success(factory(value.Trim()))
            : new Result<T, string>.Failure($"{displayName} cannot be empty");
}
