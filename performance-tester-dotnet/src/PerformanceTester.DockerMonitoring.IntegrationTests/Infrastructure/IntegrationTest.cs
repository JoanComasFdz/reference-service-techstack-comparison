using PerformanceTester.IntegrationTesting;
using Xunit.Abstractions;

namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for Docker monitoring integration tests.
/// Provides Docker monitoring system with real container infrastructure.
/// Automatically handles container initialization via Phase 0 shared infrastructure.
/// Uses primary constructor syntax (C# 12).
/// </summary>
/// <remarks>
/// No lifecycle overrides needed - DockerMonitoringSystem.Dispose() handles all cleanup.
/// This follows the same pattern as ProcessMonitoring and EventConsuming slices.
/// </remarks>
public abstract class IntegrationTest(ITestOutputHelper output)
    : IntegrationTestBase<DockerMonitoringSystem>(output)
{
}
