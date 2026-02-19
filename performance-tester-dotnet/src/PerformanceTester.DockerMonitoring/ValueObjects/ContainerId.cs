using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.ValueObjects;

/// <summary>
/// Value object for Docker container IDs (full SHA256 hash).
/// Wraps the raw string from <c>ContainerStatsResponse.ID</c>.
/// </summary>
public sealed record ContainerId : NonEmptyString
{
    private ContainerId(string value) : base(value) { }

    public static Result<ContainerId, string> Create(string value) => Create(value, "Container ID", v => new ContainerId(v));

    public static ContainerId FromString(string value) => new(value);
}
