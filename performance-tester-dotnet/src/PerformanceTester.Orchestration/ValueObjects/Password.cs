using JoanComasFdz.Result;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record Password : NonEmptyString
{
    private Password(string value) : base(value) { }

    public static Result<Password, string> Create(string value) => Create(value, "Password", v => new Password(v));

    /// <summary>Never expose password in logs or ToString().</summary>
    public override string ToString() => "***";
}
