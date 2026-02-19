using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.ValueObjects;

/// <summary>
/// Value object for memory usage in megabytes (>= 0).
/// Wraps the calculated value from Docker's <c>MemoryStats.Usage</c>.
/// </summary>
public sealed record MemoryMB : NonNegativeDouble
{
    private MemoryMB(double value) : base(value) { }

    public static Result<MemoryMB, string> Create(double value) => Create(value, "Memory MB", v => new MemoryMB(v));

    public static MemoryMB FromDouble(double value) => new(value);
}
