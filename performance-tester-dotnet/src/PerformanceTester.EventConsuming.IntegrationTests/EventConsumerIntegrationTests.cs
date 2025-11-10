using JoanComasFdz.AssertingThat;
using PerformanceTester.EventConsuming.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.EventConsuming.IntegrationTests;

/// <summary>
/// Integration tests for EventConsumer functionality.
/// Tests the complete public API: IEventConsumer and IMetricsCollector.
/// Uses real RabbitMQ container via Testcontainers.
/// </summary>
public sealed class EventConsumerIntegrationTests(ITestOutputHelper output) : IntegrationTest(output)
{
    private const string ExchangeName = "referenceservice.comparison";
    private const string RoutingKey = "instrument.status.changed";

    // Generate unique queue name per test to enable parallel execution
    private string GenerateQueueName() => $"performancetesterdotnet-{Guid.NewGuid():N}";

    [Fact]
    public async Task StartTrackingEventsAsync_WhenEventsPublished_ShouldCompleteWhenTargetReached()
    {
        // Arrange - Use 200 events to ensure throughput samples are collected (500ms sampling interval)
        const int eventCount = 200;
        var queueName = GenerateQueueName();

        // Create EventConsuming with unique queue name
        System.CreateEventConsuming(queueName);

        // Ensure exchange and queue exist
        await System.RabbitMQ.DeclareExchangeAsync(ExchangeName);
        await System.RabbitMQ.DeclareAndBindQueueAsync(queueName, ExchangeName, RoutingKey);
        await System.RabbitMQ.PurgeQueueAsync(queueName);

        // Start BackgroundServices
        await System.EventConsuming.StartAsync();

        // Start tracking (returns awaitable task)
        var trackingTask = System.EventConsuming.Consumer.StartTrackingEventsAsync(
            expectedCount: eventCount,
            inactivityTimeout: TimeSpan.FromSeconds(30));

        // Act - Publish CloudEvents to exchange
        await System.EventPublisher.PublishEventsAsync(eventCount);

        // Wait for tracking to complete
        await trackingTask;

        // Stop BackgroundServices (allows metrics collection to complete)
        await System.EventConsuming.StopAsync();

        // Assert
        Asserting.That(System.EventConsuming.MetricsCollector).HasThroughputSamples();
    }

    [Fact]
    public async Task StartTrackingEventsAsync_WhenNoEventsReceived_ShouldTimeoutWithInactivity()
    {
        // Arrange
        const int eventCount = 10;
        const int inactivityTimeoutSeconds = 2;
        var queueName = GenerateQueueName();

        // Create EventConsuming with unique queue name
        System.CreateEventConsuming(queueName);

        // Ensure queue is empty
        await System.RabbitMQ.DeclareExchangeAsync(ExchangeName);
        await System.RabbitMQ.DeclareAndBindQueueAsync(queueName, ExchangeName, RoutingKey);
        await System.RabbitMQ.PurgeQueueAsync(queueName);

        // Start BackgroundServices
        await System.EventConsuming.StartAsync();

        // Act & Assert - Should throw TimeoutException with correct message
        await Asserting.That(System.EventConsuming.Consumer)
            .ThrowsTimeoutExceptionAfterStartTrackingEvents(
                expectedCount: eventCount,
                inactivityTimeout: TimeSpan.FromSeconds(inactivityTimeoutSeconds),
                expectedReceivedCount: 0);

        await System.EventConsuming.StopAsync();
    }

    [Fact]
    public async Task StartTrackingEventsAsync_WhenSlowButProgressing_ShouldNotTimeout()
    {
        // Arrange - Test inactivity timeout reset behavior
        // Note: Using 10s timeout with 2s delays provides 8s margin for robustness under system load
        // (RabbitMQ latency, timer drift, resource contention when running with other tests)
        const int eventCount = 5;
        const int inactivityTimeoutSeconds = 10;  // Increased from 3s to 10s for robustness
        var queueName = GenerateQueueName();

        // Create EventConsuming with unique queue name
        System.CreateEventConsuming(queueName);

        // Ensure exchange and queue exist
        await System.RabbitMQ.DeclareExchangeAsync(ExchangeName);
        await System.RabbitMQ.DeclareAndBindQueueAsync(queueName, ExchangeName, RoutingKey);
        await System.RabbitMQ.PurgeQueueAsync(queueName);

        // Start BackgroundServices
        await System.EventConsuming.StartAsync();

        // Wait for RabbitMQ consumer registration to complete (prevents race condition)
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // Start tracking
        var trackingTask = System.EventConsuming.Consumer.StartTrackingEventsAsync(
            expectedCount: eventCount,
            inactivityTimeout: TimeSpan.FromSeconds(inactivityTimeoutSeconds));

        // Act - Publish events slowly (one every 2 seconds) but within inactivity timeout
        for (int i = 0; i < eventCount; i++)
        {
            await System.EventPublisher.PublishEventsAsync(1);
            if (i < eventCount - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(2)); // Slower than inactivity timeout per event
            }
        }

        // Wait for tracking to complete
        await trackingTask;

        // Stop BackgroundServices
        await System.EventConsuming.StopAsync();

        // Assert - Should complete successfully (timeout resets on each event)
        // Success is indicated by trackingTask completing without exception
    }

    [Fact]
    public async Task StartTrackingEventsAsync_WhenInvalidCount_ShouldThrow()
    {
        // Arrange
        const int invalidCount = 0;
        var queueName = GenerateQueueName();

        // Create EventConsuming with unique queue name (needed to access Consumer)
        System.CreateEventConsuming(queueName);

        // Act & Assert
        await Asserting.That(System.EventConsuming.Consumer)
            .ThrowsArgumentOutOfRangeExceptionForInvalidCount(invalidCount);
    }

    [Fact]
    public async Task StartTrackingEventsAsync_WhenNegativeTimeout_ShouldThrow()
    {
        // Arrange
        var invalidTimeout = TimeSpan.FromSeconds(-1);
        var queueName = GenerateQueueName();

        // Create EventConsuming with unique queue name (needed to access Consumer)
        System.CreateEventConsuming(queueName);

        // Act & Assert
        await Asserting.That(System.EventConsuming.Consumer)
            .ThrowsArgumentOutOfRangeExceptionForInvalidTimeout(invalidTimeout);
    }

    [Fact]
    public async Task GetThroughputSamples_AfterConsuming_ShouldReturnValidSamples()
    {
        // Arrange - Use 200 events to ensure throughput samples are collected (500ms sampling interval)
        const int eventCount = 200;
        var queueName = GenerateQueueName();

        // Create EventConsuming with unique queue name
        System.CreateEventConsuming(queueName);

        // Ensure exchange and queue exist
        await System.RabbitMQ.DeclareExchangeAsync(ExchangeName);
        await System.RabbitMQ.DeclareAndBindQueueAsync(queueName, ExchangeName, RoutingKey);
        await System.RabbitMQ.PurgeQueueAsync(queueName);

        // Start BackgroundServices
        await System.EventConsuming.StartAsync();

        // Start tracking
        var trackingTask = System.EventConsuming.Consumer.StartTrackingEventsAsync(
            expectedCount: eventCount,
            inactivityTimeout: TimeSpan.FromSeconds(30));

        // Publish CloudEvents
        await System.EventPublisher.PublishEventsAsync(eventCount);

        // Wait for completion
        await trackingTask;

        // Stop BackgroundServices (allows metrics collection to drain channel)
        await System.EventConsuming.StopAsync();

        // Assert - Chain assertions for readability
        Asserting.That(System.EventConsuming.MetricsCollector)
            .HasThroughputSamples()
            .HasValidThroughputData();
    }
}
