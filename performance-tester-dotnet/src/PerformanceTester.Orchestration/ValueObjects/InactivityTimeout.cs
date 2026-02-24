using PerformanceTester.Functional;

namespace PerformanceTester.Orchestration.ValueObjects;

public sealed record InactivityTimeout : DurationValue
{
    private InactivityTimeout(TimeSpan value) : base(value) { }

    public static Result<InactivityTimeout, string> Create(string duration) =>
        Create(duration, "inactivity timeout", v => new InactivityTimeout(v));

    public static InactivityTimeout FromTimeSpan(TimeSpan value) => new(value);
}
