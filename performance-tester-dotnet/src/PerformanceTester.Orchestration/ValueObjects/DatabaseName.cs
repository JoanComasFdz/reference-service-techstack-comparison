using JoanComasFdz.Result;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record DatabaseName : NonEmptyString
{
    private DatabaseName(string value) : base(value) { }

    public static Result<DatabaseName, string> Create(string value) => Create(value, "Database name", v => new DatabaseName(v));

    public static DatabaseName FromString(string value) => new(value);
}
