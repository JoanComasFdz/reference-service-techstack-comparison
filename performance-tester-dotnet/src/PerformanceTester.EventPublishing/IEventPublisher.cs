namespace PerformanceTester.EventPublishing;

/// <summary>
/// Service for publishing CloudEvents to RabbitMQ.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes multiple events to RabbitMQ as fast as possible.
    /// </summary>
    /// <param name="count">Number of events to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Publishing metrics including total duration and throughput.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Count is less than 1.</exception>
    /// <exception cref="InvalidOperationException">Publishing failed after retries.</exception>
    Task<PublishMetrics> PublishEventsAsync(int count, CancellationToken cancellationToken = default);
}
