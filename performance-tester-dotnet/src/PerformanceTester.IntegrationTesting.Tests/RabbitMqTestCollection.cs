using Xunit;

namespace PerformanceTester.IntegrationTesting.Tests;

/// <summary>
/// Defines a test collection that disables parallel execution for tests that use RabbitMQ.
/// All tests in this collection will run serially to avoid interference with other test projects.
/// </summary>
[CollectionDefinition("RabbitMQ Tests", DisableParallelization = true)]
public class RabbitMqTestCollection
{
    // This class is never instantiated - it's just a marker for xUnit
}
