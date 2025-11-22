using Xunit;
using Xunit.Abstractions;
using PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

namespace PerformanceTester.Orchestration.IntegrationTests;

public sealed class OrchestratorCancellationTests(ITestOutputHelper output)
    : IntegrationTest(output)
{
    [Fact]
    public async Task RunTestAsync_WhenCancelled_ShouldStopGracefully()
    {
        // Arrange
        await System.DotNetAotService.StartAsync(OrchestrationSystem.IntegrationTestDatabaseName);

        var config = new TestConfigurationBuilder()
            .WithEventCount(10000)                       // Large count
            .WithApiDuration(TimeSpan.FromMinutes(5))    // Long duration
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(2)); // Cancel after 2s

        try
        {
            // Act & Assert
            // When cancelled, the event consumer throws TimeoutException because no events are received
            // (publishing stops due to cancellation, so consumer detects inactivity)
            await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await System.Orchestration.Orchestrator.RunTestAsync(config, cts.Token);
            });

            System.DotNetAotService.Stop();
        }
        finally
        {
            // CRITICAL: Purge ConfigurableReferenceService queue to prevent stale events
            // from affecting subsequent tests. Events published to the shared exchange
            // are routed to ALL bound queues, including this one if it exists.
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }
}
