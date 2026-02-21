using JoanComasFdz.AssertingThat;
using PerformanceTester.EventPublishing.IntegrationTests.Infrastructure;
using PerformanceTester.Infrastructure.ValueObjects;
using RabbitMQ.Client;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.EventPublishing.IntegrationTests;

public sealed class EventPublisherTests(ITestOutputHelper output) : IntegrationTest(output)
{
    [Fact]
    public async Task PublishEventsAsync_WhenPublishing10Events_ShouldSucceed()
    {
        // Arrange
        var eventCount = EventCount.Create(10).SuccessValue;

        // Act
        var metrics = await System.EventPublishing.PublishEvents(eventCount);

        // Assert
        Asserting.That(metrics).HasPublishedSuccessfully(eventCount.Value);
    }

    [Fact]
    public async Task PublishEventsAsync_WhenPublishing100Events_ShouldHaveReasonableThroughput()
    {
        // Arrange
        var eventCount = EventCount.Create(500).SuccessValue;

        // Act
        var metrics = await System.EventPublishing.PublishEvents(eventCount);

        // Assert - Should publish at least 500 events/sec with pipelined publishing
        Asserting.That(metrics)
            .HasPublishedSuccessfully(eventCount.Value)
            .HasMinimumThroughput(500.0);
    }

    [Fact]
    public void PublishEventsAsync_WhenCountIsZero_ShouldBeAcceptedByEventCount()
    {
        // Act & Assert — EventCount.Create accepts zero (non-negative constraint only)
        var result = EventCount.Create(0);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void PublishEventsAsync_WhenCountIsNegative_ShouldBeRejectedByEventCount()
    {
        // Act & Assert — EventCount.Create rejects negative values at construction
        var result = EventCount.Create(-1);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task PublishEventsAsync_WhenPublishing_ShouldCreateMessagesInRabbitMQ()
    {
        // Arrange
        var eventCount = EventCount.Create(5).SuccessValue;
        const string queueName = "instrument.status.changed";
        const string exchangeName = "referenceservice.comparison";
        const string routingKey = "instrument.status.changed";

        // Create queue and bind to exchange
        var factory = new ConnectionFactory { Uri = new Uri(System.RabbitMQ.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        // Declare exchange (idempotent)
        await channel.ExchangeDeclareAsync(exchangeName, "topic", durable: true, autoDelete: false);

        // Declare queue and bind to exchange
        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(queueName, exchangeName, routingKey);

        // Clear queue before test
        await channel.QueuePurgeAsync(queueName);

        // Act
        await System.EventPublishing.PublishEvents(eventCount);

        // Wait a bit for messages to route
        await Task.Delay(100);

        // Assert - Verify messages appeared in queue
        var queueInfo = await channel.QueueDeclarePassiveAsync(queueName);
        Assert.True(
            queueInfo.MessageCount >= eventCount.Value,
            $"Expected at least {eventCount} messages in queue, but found {queueInfo.MessageCount}");
    }

    [Fact]
    public async Task PublishEventsAsync_WhenCancelled_ShouldThrowOperationCanceledException()
    {
        // Arrange
        var largeEventCount = EventCount.Create(10000).SuccessValue; // Large count to ensure cancellation happens during publishing
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100)); // Cancel after 100ms

        // Act & Assert
        // Verify that cancellation is NOT wrapped in InvalidOperationException
        // Use ThrowsAnyAsync to accept OperationCanceledException or derived types (e.g., TaskCanceledException)
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await System.EventPublishing.PublishEvents(largeEventCount, cts.Token);
        });
    }
}
