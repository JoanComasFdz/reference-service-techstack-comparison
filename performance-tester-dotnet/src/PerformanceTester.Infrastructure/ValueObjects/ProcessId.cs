using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Infrastructure.ValueObjects.ProcessId, string>;

namespace PerformanceTester.Infrastructure.ValueObjects;

/// <summary>
/// Value object representing a valid operating system process ID (> 0).
/// Invalid states are unrepresentable — use <see cref="Create(int)"/> at parse boundaries.
/// </summary>
public sealed record ProcessId
{
    /// <summary>The validated process ID.</summary>
    public int Value { get; }

    private ProcessId(int value) => Value = value;

    /// <summary>
    /// Creates a ProcessId from an integer value. Returns Failure if not positive.
    /// </summary>
    public static Result<ProcessId, string> Create(int value) => value > 0
            ? new Success(new ProcessId(value))
            : new Failure($"Process ID must be positive (got: {value})");

    /// <summary>
    /// Creates a ProcessId from an integer that is assumed valid (e.g., from OS APIs).
    /// </summary>
    public static ProcessId FromInt(int value) => new(value);

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
