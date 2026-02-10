using JoanComasFdz.Result;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record ResultsSourceFolder : ExistingFolderPath
{
    private ResultsSourceFolder(string value) : base(value) { }

    public static Result<ResultsSourceFolder, string> Create(string value) =>
        Create(value, "Results folder", v => new ResultsSourceFolder(v));
}
