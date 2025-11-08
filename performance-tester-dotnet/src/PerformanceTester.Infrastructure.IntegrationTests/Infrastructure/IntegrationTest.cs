using PerformanceTester.IntegrationTesting;
using Xunit.Abstractions;

namespace PerformanceTester.Infrastructure.IntegrationTests.Infrastructure;

public abstract class IntegrationTest(ITestOutputHelper output) : IntegrationTestBase<InfrastructureSystem>(output)
{
}
