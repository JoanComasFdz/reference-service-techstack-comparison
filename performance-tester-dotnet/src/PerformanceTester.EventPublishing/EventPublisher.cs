using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace PerformanceTester.EventPublishing;

/// <summary>
/// Static operations for publishing CloudEvents to RabbitMQ (G1, G2).
/// All dependencies are passed as explicit parameters.
/// </summary>
internal static class EventPublisher
{
    public static async Task<PublishMetrics> PublishEventsAsync(
        RabbitMqPublisher publisher,
        int count,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be at least 1");
        }

        logger.LogInformation("=== Publishing {Count} events ===", count);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            const int batchSize = 100;
            var properties = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent,
                ContentType = "application/json"
            };

            var publishTasks = new List<ValueTask>(batchSize);

            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var cloudEvent = CloudEventFactory.CreateRandomEvent();
                var body = CloudEventFactory.Serialize(cloudEvent);

                publishTasks.Add(publisher.PublishDirectAsync(properties, body, cancellationToken));

                if (publishTasks.Count >= batchSize)
                {
                    foreach (var task in publishTasks)
                    {
                        await task;
                    }

                    publishTasks.Clear();
                }

                // Log progress every 1000 events
                if ((i + 1) % 1000 == 0)
                {
                    logger.LogDebug("Published {Current}/{Total} events", i + 1, count);
                }
            }

            // Flush remaining
            foreach (var task in publishTasks)
            {
                await task;
            }

            stopwatch.Stop();

            var eventsPerSecond = count / stopwatch.Elapsed.TotalSeconds;

            logger.LogInformation(
                "Published {Count} events in {Duration:F2}s ({Throughput:F2} events/sec)",
                count,
                stopwatch.Elapsed.TotalSeconds,
                eventsPerSecond);

            return new PublishMetrics(
                EventCount: count,
                Duration: stopwatch.Elapsed,
                EventsPerSecond: eventsPerSecond);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            logger.LogWarning("Event publishing cancelled after {Duration:F2}s", stopwatch.Elapsed.TotalSeconds);
            throw; // Re-throw without wrapping - cancellation is normal control flow
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "Failed to publish events after {Duration:F2}s", stopwatch.Elapsed.TotalSeconds);
            throw new InvalidOperationException($"Failed to publish events: {ex.Message}", ex);
        }
    }
}
