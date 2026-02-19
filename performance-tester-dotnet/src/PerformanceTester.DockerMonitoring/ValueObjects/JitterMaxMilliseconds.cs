using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.ValueObjects;

public sealed record JitterMaxMilliseconds : NonNegativeInt
{
    private JitterMaxMilliseconds(int value) : base(value) { }

    public static Result<JitterMaxMilliseconds, string> Create(int value) => Create(value, "Jitter Max Milliseconds", v => new JitterMaxMilliseconds(v));

    public static JitterMaxMilliseconds FromInt(int value) => new(value);
}