using JoanComasFdz.AssertingThat;
using PerformanceTester.EventPublishing.IntegrationTests.Infrastructure;
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
        const int eventCount = 10;

        // Act
        var metrics = await System.EventPublishing.Publisher.PublishEventsAsync(eventCount);

        // Assert
        Asserting.That(metrics).HasPublishedSuccessfully(eventCount);
    }

    [Fact]
    public async Task PublishEventsAsync_WhenPublishing100Events_ShouldHaveReasonableThroughput()
    {
        // Arrange
        const int eventCount = 100;

        // Act
        var metrics = await System.EventPublishing.Publisher.PublishEventsAsync(eventCount);

        // Assert - Should publish at least 10 events/sec (conservative baseline)
        Asserting.That(metrics)
            .HasPublishedSuccessfully(eventCount)
            .HasMinimumThroughput(10.0);
    }

    [Fact]
    public void PublishEventsAsync_WhenCountIsZero_ShouldThrow()
    {
        // Arrange
        const int invalidCount = 0;

        // Act & Assert
        Asserting.That(System.EventPublishing.Publisher).ThrowsArgumentOutOfRangeForInvalidCount(invalidCount);
    }

    [Fact]
    public void PublishEventsAsync_WhenCountIsNegative_ShouldThrow()
    {
        // Arrange
        const int invalidCount = -1;

        // Act & Assert
        Asserting.That(System.EventPublishing.Publisher).ThrowsArgumentOutOfRangeForInvalidCount(invalidCount);
    }

    [Fact]
    public async Task PublishEventsAsync_WhenPublishing_ShouldCreateMessagesInRabbitMQ()
    {
        // Arrange
        const int eventCount = 5;
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
        await System.EventPublishing.Publisher.PublishEventsAsync(eventCount);

        // Wait a bit for messages to route
        await Task.Delay(100);

        // Assert - Verify messages appeared in queue
        var queueInfo = await channel.QueueDeclarePassiveAsync(queueName);
        Assert.True(queueInfo.MessageCount >= eventCount,
            $"Expected at least {eventCount} messages in queue, but found {queueInfo.MessageCount}");
    }
}
