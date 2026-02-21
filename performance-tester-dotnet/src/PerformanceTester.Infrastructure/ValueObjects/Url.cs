using PerformanceTester.Functional;

namespace PerformanceTester.Infrastructure.ValueObjects;

/// <summary>
/// Base value object for validated absolute URLs.
/// Derived types inherit validation via <see cref="Create{T}"/>.
/// </summary>
public record Url
{
    /// <summary>The validated absolute URL string.</summary>
    public string Value { get; }

    /// <summary>Initializes a new instance with the given URL value.</summary>
    protected Url(string value) => Value = value;

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>
    /// Creates a derived <typeparamref name="T"/> from a raw string.
    /// Returns Failure if the value is empty or not a well-formed absolute URI.
    /// </summary>
    protected static Result<T, string> Create<T>(string value, string displayName, Func<string, T> factory)
        where T : Url
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Result<T, string>.Failure($"{displayName} cannot be empty");
        }

        var trimmed = value.Trim();
        if (!Uri.IsWellFormedUriString(trimmed, UriKind.Absolute))
        {
            return new Result<T, string>.Failure($"{displayName} is not a valid absolute URL: '{trimmed}'");
        }

        return new Result<T, string>.Success(factory(trimmed));
    }
}
