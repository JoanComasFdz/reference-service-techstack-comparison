using JoanComasFdz.AssertingThat;
using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.Infrastructure.IntegrationTests;

[Collection("RabbitMQ Tests")]
public sealed class RabbitMqCleanerTests(ITestOutputHelper output) : IntegrationTest(output)
{
    private readonly string _testPrefix = $"test_{Guid.NewGuid():N}_";

    private string GetTestQueueName(string baseName) => _testPrefix + baseName;

    [Fact]
    public async Task ClearAllQueuesAsync_WhenQueuesHaveMessages_ShouldPurgeAllQueues()
    {
        // Arrange - Create test queue with unique name and publish messages
        var testQueue = GetTestQueueName("single_queue");
        await System.RabbitMQ.CreateQueueWithMessagesAsync(testQueue, messageCount: 10);

        await Asserting.That(System.RabbitMQ).QueueHasMessageCount(testQueue, 10);

        // Act
        var result = await System.Infrastructure.ClearAllQueues();

        // Assert - Queue should still exist but have no messages
        Assert.IsType<Result<Unit, string>.Success>(result);
        await Asserting.That(System.RabbitMQ).QueueHasNoMessages(testQueue);

        // Cleanup
        await System.RabbitMQ.DeleteQueueAsync(testQueue);
    }

    [Fact]
    public async Task ClearAllQueuesAsync_WhenNoQueuesExist_ShouldNotThrow()
    {
        // Arrange - No queues created for this test (ClearAllQueuesAsync should handle empty state gracefully)
        // Note: We intentionally don't delete all queues to avoid interfering with parallel tests

        // Act
        var result = await System.Infrastructure.ClearAllQueues();

        // Assert
        Assert.IsType<Result<Unit, string>.Success>(result);
    }

    [Fact]
    public async Task ClearAllQueuesAsync_WhenMultipleQueuesExist_ShouldPurgeAll()
    {
        // Arrange - Create multiple test queues with unique names
        var queue1 = GetTestQueueName("queue1");
        var queue2 = GetTestQueueName("queue2");
        var queue3 = GetTestQueueName("queue3");

        // Create queues with messages
        await System.RabbitMQ.CreateQueueWithMessagesAsync(queue1, 5);
        await System.RabbitMQ.CreateQueueWithMessagesAsync(queue2, 3);
        await System.RabbitMQ.CreateQueueWithMessagesAsync(queue3, 7);

        // Verify messages exist
        await Asserting.That(System.RabbitMQ).QueueHasMessageCount(queue1, 5);
        await Asserting.That(System.RabbitMQ).QueueHasMessageCount(queue2, 3);
        await Asserting.That(System.RabbitMQ).QueueHasMessageCount(queue3, 7);

        // Act
        var result = await System.Infrastructure.ClearAllQueues();

        // Assert - All queues should have no messages
        Assert.IsType<Result<Unit, string>.Success>(result);
        await Asserting.That(System.RabbitMQ).QueueHasNoMessages(queue1);
        await Asserting.That(System.RabbitMQ).QueueHasNoMessages(queue2);
        await Asserting.That(System.RabbitMQ).QueueHasNoMessages(queue3);

        // Cleanup
        await System.RabbitMQ.DeleteQueueAsync(queue1);
        await System.RabbitMQ.DeleteQueueAsync(queue2);
        await System.RabbitMQ.DeleteQueueAsync(queue3);
    }

    [Fact]
    public async Task ClearAllQueuesAsync_WhenQueueNamesHaveSpecialCharacters_ShouldHandleCorrectly()
    {
        // Arrange - Create queues with special characters that need URL encoding
        // Note: Using unique prefix to avoid interference with parallel tests
        var queue1 = GetTestQueueName("queue-with-dashes");
        var queue2 = GetTestQueueName("queue_with_underscores");
        var queue3 = GetTestQueueName("queue.with.dots");

        await System.RabbitMQ.CreateQueueWithMessagesAsync(queue1, 2);
        await System.RabbitMQ.CreateQueueWithMessagesAsync(queue2, 2);
        await System.RabbitMQ.CreateQueueWithMessagesAsync(queue3, 2);

        // Act
        var result = await System.Infrastructure.ClearAllQueues();

        // Assert - All queues should have no messages
        Assert.IsType<Result<Unit, string>.Success>(result);
        await Asserting.That(System.RabbitMQ).QueueHasNoMessages(queue1);
        await Asserting.That(System.RabbitMQ).QueueHasNoMessages(queue2);
        await Asserting.That(System.RabbitMQ).QueueHasNoMessages(queue3);

        // Cleanup
        await System.RabbitMQ.DeleteQueueAsync(queue1);
        await System.RabbitMQ.DeleteQueueAsync(queue2);
        await System.RabbitMQ.DeleteQueueAsync(queue3);
    }

    [Fact]
    public async Task ClearAllQueuesAsync_WhenEmptyQueuesExist_ShouldNotThrow()
    {
        // Arrange - Create empty queues with unique names
        var queue1 = GetTestQueueName("empty_queue_1");
        var queue2 = GetTestQueueName("empty_queue_2");

        await System.RabbitMQ.CreateQueueWithMessagesAsync(queue1, 0);
        await System.RabbitMQ.CreateQueueWithMessagesAsync(queue2, 0);

        // Act
        var result = await System.Infrastructure.ClearAllQueues();

        // Assert
        Assert.IsType<Result<Unit, string>.Success>(result);
        await Asserting.That(System.RabbitMQ).QueueHasNoMessages(queue1);
        await Asserting.That(System.RabbitMQ).QueueHasNoMessages(queue2);

        // Cleanup
        await System.RabbitMQ.DeleteQueueAsync(queue1);
        await System.RabbitMQ.DeleteQueueAsync(queue2);
    }
}
