using PerformanceTester.IntegrationTesting;
using Xunit.Abstractions;

namespace PerformanceTester.ProcessMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for ProcessMonitoring integration tests.
/// Inherits from IntegrationTestBase and provides access to ProcessMonitoring services.
/// Automatically handles container initialization via Phase 0 shared infrastructure.
/// Uses primary constructor syntax (C# 12).
/// </summary>
public abstract class IntegrationTest(ITestOutputHelper output)
    : IntegrationTestBase<ProcessMonitoringSystem>(output)
{
}
