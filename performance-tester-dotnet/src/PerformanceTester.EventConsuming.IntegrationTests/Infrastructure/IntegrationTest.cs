using PerformanceTester.IntegrationTesting;
using Xunit.Abstractions;

namespace PerformanceTester.EventConsuming.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for EventConsuming integration tests.
/// Inherits from IntegrationTestBase and provides access to EventConsuming services.
/// Uses primary constructor syntax (C# 12).
/// </summary>
public abstract class IntegrationTest(ITestOutputHelper output)
    : IntegrationTestBase<EventConsumingSystem>(output)
{
}
