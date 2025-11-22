using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for orchestration integration tests.
/// Provides access to OrchestrationSystem with all dependencies.
/// Lifecycle managed by OrchestrationSystem.Dispose().
/// </summary>
public abstract class IntegrationTest(ITestOutputHelper output)
    : IntegrationTesting.IntegrationTestBase<OrchestrationSystem>(output)
{
    // Intentionally empty - lifecycle managed by OrchestrationSystem
}
