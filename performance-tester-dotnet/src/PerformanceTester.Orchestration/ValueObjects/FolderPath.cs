namespace PerformanceTester.Orchestration.ValueObjects;

public record FolderPath : NonEmptyString
{
    protected FolderPath(string value) : base(value) { }
}
