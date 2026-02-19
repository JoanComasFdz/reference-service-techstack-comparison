using PerformanceTester.Functional;

namespace PerformanceTester.Reporting.ValueObjects;

public sealed record ResultsOutputFolder : FolderPath
{
    private ResultsOutputFolder(string value) : base(value) { }

    public static Result<ResultsOutputFolder, string> Create(string value) => Create(value, "Results folder", v => new ResultsOutputFolder(v));

    public static ResultsOutputFolder FromString(string value) => new(value);
}
