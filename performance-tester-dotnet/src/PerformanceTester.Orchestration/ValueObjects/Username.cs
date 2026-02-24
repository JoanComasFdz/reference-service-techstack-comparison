using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record Username : NonEmptyString
{
    private Username(string value) : base(value) { }

    public static Result<Username, string> Create(string value) => Create(value, "Username", v => new Username(v));

    public static Username FromString(string value) => new(value);
}
