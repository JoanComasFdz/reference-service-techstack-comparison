using JoanComasFdz.AssertingThat;
using PerformanceTester.EventConsuming;
using PerformanceTester.EventConsuming.IntegrationTests.Infrastructure;
using PerformanceTester.Infrastructure.ValueObjects;
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
        await System.PublishEvents(EventCount.Create(eventCount).SuccessValue);

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
        const int eventCount = 5;
        const int inactivityTimeoutSeconds = 10;
        var queueName = GenerateQueueName();

        // Create EventConsuming with unique queue name
        System.CreateEventConsuming(queueName);

        // Ensure exchange and queue exist
        await System.RabbitMQ.DeclareExchangeAsync(ExchangeName);
        await System.RabbitMQ.DeclareAndBindQueueAsync(queueName, ExchangeName, RoutingKey);
        await System.RabbitMQ.PurgeQueueAsync(queueName);

        // Start BackgroundServices
        await System.EventConsuming.StartAsync();

        // Create phase awaiter for deterministic synchronization
        var phaseAwaiter = new ConsumerPhaseAwaiter();

        // Start tracking (returns awaitable task)
        var trackingTask = System.EventConsuming.Consumer.StartTrackingEventsAsync(
            expectedCount: eventCount,
            inactivityTimeout: TimeSpan.FromSeconds(inactivityTimeoutSeconds),
            progress: phaseAwaiter);

        // Wait for tracking to actually start before publishing
        // This replaces the arbitrary Task.Delay(500ms)
        await phaseAwaiter.WaitForTrackingStartedAsync(timeout: TimeSpan.FromSeconds(10));

        // Act - Publish events slowly (one every 2 seconds) but within inactivity timeout
        for (int i = 0; i < eventCount; i++)
        {
            await System.PublishEvents(EventCount.Create(1).SuccessValue);

            // Wait for event to be received before continuing
            // This ensures the inactivity timer is properly reset
            await phaseAwaiter.WaitForEventCountAsync(i + 1, timeout: TimeSpan.FromSeconds(15));

            if (i < eventCount - 1)
            {
                // Simulate slow publisher - delay between events
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        // Wait for tracking to complete
        await trackingTask;

        // Stop BackgroundServices
        await System.EventConsuming.StopAsync();

        // Assert - Verify phase sequence
        phaseAwaiter.AssertPhasesReceivedInOrder(
            (ConsumerPhase.TrackingStarted, ConsumerPhaseState.Starting),
            (ConsumerPhase.TargetReached, ConsumerPhaseState.Completed));

        phaseAwaiter.AssertEventCountAtLeast(eventCount);
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
        // Arrange - Use enough events to ensure throughput samples are collected
        // The 500ms sampling interval means we need events spread over at least 1 second
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

        // Create phase awaiter for deterministic synchronization
        var phaseAwaiter = new ConsumerPhaseAwaiter();

        // Start tracking
        var trackingTask = System.EventConsuming.Consumer.StartTrackingEventsAsync(
            expectedCount: eventCount,
            inactivityTimeout: TimeSpan.FromSeconds(30),
            progress: phaseAwaiter);

        // Wait for tracking to start
        await phaseAwaiter.WaitForTrackingStartedAsync(timeout: TimeSpan.FromSeconds(10));

        // Publish CloudEvents
        await System.PublishEvents(EventCount.Create(eventCount).SuccessValue);

        // Wait for completion using phase event (not timing assumption)
        await phaseAwaiter.WaitForTargetReachedAsync(timeout: TimeSpan.FromSeconds(30));
        await trackingTask;

        // Stop BackgroundServices (allows metrics collection to drain channel)
        await System.EventConsuming.StopAsync();

        // Assert - Verify phase sequence completed correctly
        phaseAwaiter.AssertPhasesReceivedInOrder(
            (ConsumerPhase.TrackingStarted, ConsumerPhaseState.Starting),
            (ConsumerPhase.TargetReached, ConsumerPhaseState.Completed));

        // Verify throughput data
        Asserting.That(System.EventConsuming.MetricsCollector)
            .HasThroughputSamples()
            .HasValidThroughputData();
    }
}
