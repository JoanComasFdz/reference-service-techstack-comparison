using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.ValueObjects;

/// <summary>
/// Value object for CPU usage percentage (>= 0, can exceed 100% on multi-core systems).
/// Wraps the calculated value from <see cref="StatsProcessing.CalculateCpuPercent"/>.
/// </summary>
public sealed record CpuPercent : NonNegativeDouble
{
    private CpuPercent(double value) : base(value) { }

    public static Result<CpuPercent, string> Create(double value) => Create(value, "CPU percent", v => new CpuPercent(v));

    public static CpuPercent FromDouble(double value) => new(value);
}
