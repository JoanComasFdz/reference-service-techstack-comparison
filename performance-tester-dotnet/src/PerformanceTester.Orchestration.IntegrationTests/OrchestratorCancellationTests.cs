using Xunit;
using Xunit.Abstractions;
using PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

namespace PerformanceTester.Orchestration.IntegrationTests;

public sealed class OrchestratorCancellationTests(ITestOutputHelper output)
    : IntegrationTest(output)
{
    [Fact]
    public async Task RunTestAsync_WhenCancelledDuringEventPhase_ShouldStopGracefully()
    {
        // Arrange
        const int testPort = 9994;
        const int warmupEventCount = 10;  // Small warmup to quickly get to event phase

        // Configure to publish many events (more than will be consumed before cancellation)
        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: 10000,
            warmupEventCount: warmupEventCount);

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        var config = new TestConfigurationBuilder()
            .WithEventCount(10000)                       // Large count to ensure we're in event phase
            .WithWarmupEventCount(warmupEventCount)      // Must match ConfigurableReferenceService
            .WithApiDuration(TimeSpan.FromSeconds(1))    // Short (won't reach this phase)
            .WithServicePort(testPort)
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(2)); // Cancel while processing events

        try
        {
            // Act & Assert
            // When cancelled during event phase, the orchestrator should stop gracefully.
            // The exact exception type depends on which component detects cancellation first:
            // - Publisher: OperationCanceledException (from ThrowIfCancellationRequested)
            // - Consumer: TaskCanceledException (from TrySetCanceled)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await System.Orchestration.Orchestrator.RunTestAsync(config, cancellationToken: cts.Token);
            });
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    [Fact]
    public async Task RunTestAsync_WhenCancelledDuringApiPhase_ShouldStopGracefully()
    {
        // Arrange
        const int testPort = 9995;

        // Configure minimal events to quickly reach API phase
        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: 1,
            warmupEventCount: 50);  // Match default warmup count

        await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: testPort);
        await System.WaitForServiceHealthyAsync(port: testPort);

        var config = new TestConfigurationBuilder()
            .WithEventCount(1)                           // Minimal events to quickly reach API phase
            .WithApiDuration(TimeSpan.FromMinutes(1))    // Long duration to ensure we're in API phase
            .WithServicePort(testPort)
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(5)); // Cancel while in API load test phase

        try
        {
            // Act & Assert
            // When cancelled during API phase, the orchestrator should stop gracefully.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await System.Orchestration.Orchestrator.RunTestAsync(config, cancellationToken: cts.Token);
            });
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }
}
