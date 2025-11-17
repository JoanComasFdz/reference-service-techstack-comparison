using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace PerformanceTester.EventPublishing;

/// <summary>
/// Main service for publishing CloudEvents to RabbitMQ.
/// Implements IEventPublisher interface.
/// </summary>
internal sealed class EventPublisher : IEventPublisher
{
    private readonly RabbitMqPublisher _publisher;
    private readonly CloudEventFactory _factory;
    private readonly ILogger<EventPublisher> _logger;

    public EventPublisher(
        RabbitMqPublisher publisher,
        CloudEventFactory factory,
        ILogger<EventPublisher> logger)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
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
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var cloudEvent = _factory.CreateRandomEvent();
                await _publisher.PublishEventAsync(cloudEvent, cancellationToken);

                // Log progress every 1000 events
                if ((i + 1) % 1000 == 0)
                {
                    _logger.LogDebug("Published {Current}/{Total} events", i + 1, count);
                }
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
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to publish events after {Duration:F2}s", stopwatch.Elapsed.TotalSeconds);
            throw new InvalidOperationException($"Failed to publish events: {ex.Message}", ex);
        }
    }
}
