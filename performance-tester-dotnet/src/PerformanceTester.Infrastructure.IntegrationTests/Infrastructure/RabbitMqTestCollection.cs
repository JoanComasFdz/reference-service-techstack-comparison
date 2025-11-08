using Xunit;

namespace PerformanceTester.Infrastructure.IntegrationTests.Infrastructure;

/// <summary>
/// Defines a test collection that disables parallel execution for tests that use RabbitMQ.
/// All tests in this collection will run serially to avoid interference when calling ClearAllQueuesAsync().
/// </summary>
[CollectionDefinition("RabbitMQ Tests", DisableParallelization = true)]
public class RabbitMqTestCollection
{
    // This class is never instantiated - it's just a marker for xUnit
}
