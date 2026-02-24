using PerformanceTester.Functional;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record ApiDuration : DurationValue
{
    private ApiDuration(TimeSpan value) : base(value) { }

    public static Result<ApiDuration, string> Create(string duration) =>
        Create(duration, "API duration", v => new ApiDuration(v));

    public static ApiDuration FromTimeSpan(TimeSpan value) => new(value);
}
