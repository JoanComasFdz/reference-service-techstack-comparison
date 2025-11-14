using PerformanceTester.IntegrationTesting;
using Xunit.Abstractions;

namespace PerformanceTester.Reporting.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for all Reporting integration tests.
/// Provides access to ReportingSystem (facade for Reporting services).
/// Uses primary constructor syntax (C# 12).
/// </summary>
public abstract class IntegrationTest(ITestOutputHelper output)
    : IntegrationTestBase<ReportingSystem>(output)
{
}
