using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only concrete NonEmptyString for container names.
/// Used to create NonEmptyString instances without depending on Orchestration value objects.
/// </summary>
internal sealed record TestContainerName : NonEmptyString
{
    public TestContainerName(string value) : base(value) { }
}
