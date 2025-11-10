using PerformanceTester.IntegrationTesting;
using Xunit.Abstractions;

namespace PerformanceTester.EventPublishing.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for EventPublishing integration tests.
/// Inherits from IntegrationTestBase and provides access to EventPublishing services.
/// Uses primary constructor syntax (C# 12).
/// </summary>
public abstract class IntegrationTest(ITestOutputHelper output)
    : IntegrationTestBase<EventPublishingSystem>(output)
{
}
