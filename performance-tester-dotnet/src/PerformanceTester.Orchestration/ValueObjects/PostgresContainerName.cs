using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Orchestration.ValueObjects.PostgresContainerName, string>;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record PostgresContainerName : ContainerName
{
    private PostgresContainerName(string value) : base(value) { }

    public static Result<PostgresContainerName, string> Create(string? value) =>
        IsValid(value)
            ? new Success(new PostgresContainerName(Trimmed(value!)))
            : new Failure("PostgreSQL container name cannot be empty");

    public static PostgresContainerName FromString(string value) => new(value);
}
