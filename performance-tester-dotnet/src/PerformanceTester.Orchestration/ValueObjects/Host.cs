using JoanComasFdz.Result;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record Host : NonEmptyString
{
    private Host(string value) : base(value) { }

    public static Result<Host, string> Create(string value) =>
        Create(value, "Host", v => new Host(v));
}
