namespace PerformanceTester.Orchestration.ValueObjects;

public record ContainerName
{
    public string Value { get; }
    protected ContainerName(string value) => Value = value;

    public override string ToString() => Value;

    protected static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value);
    protected static string Trimmed(string value) => value.Trim();
}
