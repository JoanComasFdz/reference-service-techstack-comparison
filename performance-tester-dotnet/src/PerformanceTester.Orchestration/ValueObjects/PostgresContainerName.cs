using JoanComasFdz.Result;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record PostgresContainerName : ContainerName
{
    private PostgresContainerName(string value) : base(value) { }

    public static Result<PostgresContainerName, string> Create(string value) =>
        Create(value, "PostgreSQL", v => new PostgresContainerName(v));

    public static PostgresContainerName FromString(string value) => new(value);
}
