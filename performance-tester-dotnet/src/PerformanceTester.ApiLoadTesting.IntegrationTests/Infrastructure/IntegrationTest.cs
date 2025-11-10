using PerformanceTester.IntegrationTesting;
using Xunit.Abstractions;

namespace PerformanceTester.ApiLoadTesting.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for ApiLoadTesting integration tests.
/// Inherits from IntegrationTestBase and provides access to ApiLoadTesting services.
/// Uses primary constructor syntax (C# 12).
/// </summary>
public abstract class IntegrationTest(ITestOutputHelper output)
    : IntegrationTestBase<ApiLoadTestingSystem>(output)
{
}
