using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Orchestration.ValueObjects.DatabaseName, string>;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record DatabaseName
{
    public string Value { get; }
    private DatabaseName(string value) => Value = value;

    public static Result<DatabaseName, string> Create(string value) =>
        !string.IsNullOrWhiteSpace(value)
            ? new Success(new DatabaseName(value.Trim()))
            : new Failure("Database name cannot be empty");

    public static DatabaseName FromString(string value) => new(value);

    public override string ToString() => Value;
}
