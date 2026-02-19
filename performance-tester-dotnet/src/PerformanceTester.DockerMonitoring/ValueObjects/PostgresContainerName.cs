using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.ValueObjects;

public sealed record PostgresContainerName : NonEmptyString
{
    private PostgresContainerName(string value) : base(value) { }

    public static Result<PostgresContainerName, string> Create(string value) => Create(value, "PostgreSQL container name", v => new PostgresContainerName(v));

    public static PostgresContainerName FromString(string value) => new(value);
}
