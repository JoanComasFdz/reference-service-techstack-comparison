using PerformanceTester.Functional;

namespace PerformanceTester.Reporting.ValueObjects;

public record FolderPath
{
    public string Value { get; }
    protected FolderPath(string value) => Value = value;

    public override string ToString() => Value;

    protected static Result<T, string> Create<T>(string value, string displayName, Func<string, T> factory)
        where T : FolderPath => !string.IsNullOrWhiteSpace(value)
            ? new Result<T, string>.Success(factory(value.Trim()))
            : new Result<T, string>.Failure($"{displayName} cannot be empty");
}
