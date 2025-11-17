namespace PerformanceTester.EventPublishing;

/// <summary>
/// Service for publishing CloudEvents to RabbitMQ.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Establishes connection to RabbitMQ.
    /// Must be called before PublishEventsAsync.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Connection failed or already connected.</exception>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gracefully disconnects from RabbitMQ.
    /// Safe to call multiple times.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes multiple events to RabbitMQ as fast as possible.
    /// </summary>
    /// <param name="count">Number of events to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Publishing metrics including total duration and throughput.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Count is less than 1.</exception>
    /// <exception cref="InvalidOperationException">Publishing failed after retries or not connected.</exception>
    Task<PublishMetrics> PublishEventsAsync(int count, CancellationToken cancellationToken = default);
}
