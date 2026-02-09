using JoanComasFdz.Result;

namespace PerformanceTester.Orchestration.ValueObjects;

public record ContainerName
{
    public string Value { get; }
    protected ContainerName(string value) => Value = value;

    public override string ToString() => Value;

    protected static Result<T, string> Create<T>(string? value, string errorMessage, Func<string, T> factory)
        where T : ContainerName =>
        !string.IsNullOrWhiteSpace(value)
            ? new Result<T, string>.Success(factory(value.Trim()))
            : new Result<T, string>.Failure(errorMessage);
}
