using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Infrastructure.ValueObjects.Port, string>;

namespace PerformanceTester.Infrastructure.ValueObjects;

/// <summary>
/// Value object representing a valid network port (1–65535).
/// Invalid states are unrepresentable — use <see cref="Create(int)"/> or <see cref="Create(string)"/> at parse boundaries.
/// </summary>
public sealed record Port
{
    /// <summary>The validated port number.</summary>
    public int Value { get; }

    private Port(int value) => Value = value;

    /// <summary>
    /// Creates a Port from an integer value. Returns Failure if not in range 1–65535.
    /// </summary>
    public static Result<Port, string> Create(int value) => value is >= 1 and <= 65535
            ? new Success(new Port(value))
            : new Failure($"Port must be between 1 and 65535 (got: {value})");

    /// <summary>
    /// Creates a Port from a string value. Returns Failure if not a valid integer or not in range 1–65535.
    /// </summary>
    public static Result<Port, string> Create(string value) => int.TryParse(value, out var parsed)
            ? Create(parsed)
            : new Failure($"Port must be a number (got: '{value}')");

    /// <summary>
    /// Creates a Port from an integer that is assumed valid (e.g., from test constants).
    /// </summary>
    public static Port FromInt(int value) => new(value);

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
