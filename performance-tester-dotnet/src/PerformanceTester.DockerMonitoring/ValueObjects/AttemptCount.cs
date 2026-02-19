using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.ValueObjects;

/// <summary>
/// Value object for connection attempt/failure counts (>= 0).
/// Used across <see cref="ConnectionState"/> variants: AttemptNumber, ConsecutiveFailures, TotalAttempts.
/// These represent the same counter flowing through the state machine lifecycle.
/// </summary>
internal sealed record AttemptCount : NonNegativeInt
{
    private AttemptCount(int value) : base(value) { }

    public static Result<AttemptCount, string> Create(int value) => Create(value, "Attempt count", v => new AttemptCount(v));

    public static AttemptCount FromInt(int value) => new(value);

    // -- Type-preserving arithmetic (shadows base NonNegativeInt operators) --

    public static AttemptCount operator +(AttemptCount left, int right) => new(Math.Max(0, left.Value + right));

    public static AttemptCount operator -(AttemptCount left, int right) => new(Math.Max(0, left.Value - right));

    public static AttemptCount operator ++(AttemptCount value) => new(value.Value + 1);

    public static AttemptCount operator --(AttemptCount value) => new(Math.Max(0, value.Value - 1));
}
