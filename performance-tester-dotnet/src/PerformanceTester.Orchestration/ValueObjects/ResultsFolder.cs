using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Orchestration.ValueObjects.ResultsFolder, string>;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record ResultsFolder
{
    public string Value { get; }
    private ResultsFolder(string value) => Value = value;

    public static Result<ResultsFolder, string> Create(string? value) =>
        !string.IsNullOrWhiteSpace(value)
            ? new Success(new ResultsFolder(value.Trim()))
            : new Failure("Results folder cannot be empty");

    public static ResultsFolder FromString(string value) => new(value);

    public override string ToString() => Value;
}
