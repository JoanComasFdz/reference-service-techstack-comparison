using Xunit;
using Xunit.Abstractions;
using PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

namespace PerformanceTester.Orchestration.IntegrationTests;

public sealed class OrchestratorErrorHandlingTests(ITestOutputHelper output)
    : IntegrationTest(output)
{
    [Fact]
    public async Task RunTestAsync_WhenServiceNotRunning_ShouldFailGracefully()
    {
        // Arrange
        // No service started - ConfigurableReferenceService not connected
        // Service discovery will timeout trying to find process on port

        var config = new TestConfigurationBuilder()
            .WithEventCount(10)
            .WithServicePort(9998)  // Use dedicated port that has no listener
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        try
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await System.Orchestration.Orchestrator.RunTestAsync(config);
            });

            // Verify error message mentions service not found
            Assert.Contains("Service not found", exception.Message);
            Assert.Contains($"port {config.ServicePort}", exception.Message);
            Assert.Contains("30 seconds", exception.Message);
        }
        finally
        {
            // CRITICAL: Purge ConfigurableReferenceService queue to prevent stale events
            // from affecting subsequent tests. Events published to the shared exchange
            // are routed to ALL bound queues, including this one if it exists.
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }

    [Fact]
    public async Task RunTestAsync_WhenConsumerTimeout_ShouldIncludeReceivedCount()
    {
        // Arrange
        // Use ConfigurableReferenceService as a minimal test service
        // Timeline:
        //   Warmup phase: Orchestrator publishes 1 events
        //                 ConfigurableReferenceService responds to each immediately (acts like real service)
        //   Test phase:   Orchestrator publishes 2 events, expects 2 back
        //                 ConfigurableReferenceService triggers on event #2 (first after 1 warmup events)
        //                 ConfigurableReferenceService publishes only 1 event → TIMEOUT!
        var config = new TestConfigurationBuilder()
            .WithWarmupEventCount(1)
            .WithWarmupApiCallCount(3)
            .WithWarmupInactivityTimeout(TimeSpan.FromSeconds(2))
            .WithEventCount(2)
            .WithInactivityTimeout(TimeSpan.FromSeconds(2))  // Short timeout to detect missing events
            .WithServicePort(9999)  // Use test port for ConfigurableReferenceService
            .WithDatabaseName(OrchestrationSystem.IntegrationTestDatabaseName)
            .Build();

        System.ConfigurableReferenceService.ConfigurePublication(
            eventCount: 1,              // Publish only 1 event (orchestrator expects 2)
            warmupEventCount: config.WarmupEventCount);

        try
        {
            // CRITICAL: Purge the queue BEFORE connecting to remove stale events from previous tests
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);

            // Make ConfigurableReferenceService discoverable on the test port (recreates its queue)
            await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: config.ServicePort.Value);

            // Wait for service to be fully ready (RabbitMQ subscription established)
            // Health check polls /health endpoint until service reports ready status
            await System.WaitForServiceHealthyAsync(port: config.ServicePort.Value, timeout: TimeSpan.FromSeconds(10));

            // Act & Assert: Should timeout with progress info
            var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await System.Orchestration.Orchestrator.RunTestAsync(config);
            });

            // Verify exception message includes progress
            Assert.Contains("Inactivity timeout", exception.Message);
            Assert.Contains("1/2", exception.Message);  // Received 1 out of 2
        }
        finally
        {
            await System.ConfigurableReferenceService.DisconnectAsync();

            // CRITICAL: Purge ConfigurableReferenceService queue to prevent stale events
            // from affecting subsequent tests.
            await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
        }
    }
}
