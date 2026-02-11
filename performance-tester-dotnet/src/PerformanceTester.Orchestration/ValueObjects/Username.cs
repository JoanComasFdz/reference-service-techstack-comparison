using JoanComasFdz.Result;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record Username : NonEmptyString
{
    private Username(string value) : base(value) { }

    public static Result<Username, string> Create(string value) =>
        Create(value, "Username", v => new Username(v));
}
