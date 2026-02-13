using JoanComasFdz.Result;

namespace PerformanceTester.Reporting.ValueObjects;

public record ExistingFolderPath
{
    public string Value { get; }
    protected ExistingFolderPath(string value) => Value = value;

    public override string ToString() => Value;

    protected static Result<T, string> Create<T>(string value, string displayName, Func<string, T> factory)
        where T : ExistingFolderPath
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Result<T, string>.Failure($"{displayName} cannot be empty");
        }

        var trimmed = value.Trim();

        if (!Directory.Exists(trimmed))
        {
            return new Result<T, string>.Failure($"{displayName} not found: {trimmed}");
        }

        return new Result<T, string>.Success(factory(trimmed));
    }
}
