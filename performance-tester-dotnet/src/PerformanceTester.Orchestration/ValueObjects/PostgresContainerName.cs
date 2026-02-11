using JoanComasFdz.Result;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record PostgresContainerName : NonEmptyString
{
    private PostgresContainerName(string value) : base(value) { }

    public static Result<PostgresContainerName, string> Create(string value) =>
        Create(value, "PostgreSQL container name", v => new PostgresContainerName(v));

    public static PostgresContainerName FromString(string value) => new(value);
}
