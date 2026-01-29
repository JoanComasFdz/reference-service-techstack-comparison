using PerformanceTester.IntegrationTesting;
using Xunit.Abstractions;

namespace PerformanceTester.SystemMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for SystemMonitoring integration tests.
/// </summary>
public abstract class IntegrationTest(ITestOutputHelper output)
    : IntegrationTestBase<SystemMonitoringSystem>(output)
{
}
