using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Orchestration.ValueObjects.ContainerName, string>;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record ContainerName
{
    public string Value { get; }
    private ContainerName(string value) => Value = value;

    public static Result<ContainerName, string> Create(string? value, string label) =>
        !string.IsNullOrWhiteSpace(value)
            ? new Success(new ContainerName(value.Trim()))
            : new Failure($"{label} container name cannot be empty");

    public static ContainerName FromString(string value) => new(value);

    public override string ToString() => Value;
}
