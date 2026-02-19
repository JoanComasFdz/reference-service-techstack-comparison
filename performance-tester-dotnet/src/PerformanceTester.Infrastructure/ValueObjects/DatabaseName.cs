using PerformanceTester.Functional;

namespace PerformanceTester.Infrastructure.ValueObjects;

/// <summary>
/// Value object representing a valid, non-empty database name.
/// Invalid states are unrepresentable — use <see cref="Create(string)"/> at parse boundaries.
/// </summary>
public sealed record DatabaseName : NonEmptyString
{
    private DatabaseName(string value) : base(value) { }

    /// <summary>
    /// Creates a DatabaseName from a raw string. Returns Failure if null, empty, or whitespace.
    /// </summary>
    public static Result<DatabaseName, string> Create(string value) => Create(value, "Database name", v => new DatabaseName(v));

    /// <summary>
    /// Creates a DatabaseName from a string that is assumed valid (e.g., from configuration).
    /// </summary>
    public static DatabaseName FromString(string value) => new(value);
}
