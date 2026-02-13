using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace PerformanceTester.EventPublishing;

/// <summary>
/// Main service for publishing CloudEvents to RabbitMQ.
/// Implements IEventPublisher interface.
/// </summary>
internal sealed class EventPublisher : IEventPublisher
{
    private readonly RabbitMqPublisher _publisher;
    private readonly ILogger<EventPublisher> _logger;

    public EventPublisher(
        RabbitMqPublisher publisher,
        ILogger<EventPublisher> logger)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _publisher.ConnectAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _publisher.DisconnectAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PublishMetrics> PublishEventsAsync(int count, CancellationToken cancellationToken = default)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be at least 1");
        }

        _logger.LogInformation("=== Publishing {Count} events ===", count);

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

                publishTasks.Add(_publisher.PublishDirectAsync(properties, body, cancellationToken));

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
                    _logger.LogDebug("Published {Current}/{Total} events", i + 1, count);
                }
            }

            // Flush remaining
            foreach (var task in publishTasks)
            {
                await task;
            }

            stopwatch.Stop();

            var eventsPerSecond = count / stopwatch.Elapsed.TotalSeconds;

            _logger.LogInformation(
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
            _logger.LogWarning("Event publishing cancelled after {Duration:F2}s", stopwatch.Elapsed.TotalSeconds);
            throw; // Re-throw without wrapping - cancellation is normal control flow
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to publish events after {Duration:F2}s", stopwatch.Elapsed.TotalSeconds);
            throw new InvalidOperationException($"Failed to publish events: {ex.Message}", ex);
        }
    }
}
