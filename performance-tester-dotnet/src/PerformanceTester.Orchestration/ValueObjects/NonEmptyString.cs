using JoanComasFdz.Result;

namespace PerformanceTester.Orchestration.ValueObjects;

public record NonEmptyString
{
    public string Value { get; }
    protected NonEmptyString(string value) => Value = value;

    public override string ToString() => Value;

    protected static Result<T, string> Create<T>(string value, string displayName, Func<string, T> factory)
        where T : NonEmptyString =>
        !string.IsNullOrWhiteSpace(value)
            ? new Result<T, string>.Success(factory(value.Trim()))
            : new Result<T, string>.Failure($"{displayName} cannot be empty");
}
