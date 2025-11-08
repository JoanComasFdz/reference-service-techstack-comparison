namespace PerformanceTester.Infrastructure;

/// <summary>
/// Service for managing RabbitMQ queues during testing.
/// </summary>
public interface IRabbitMQ
{
    /// <summary>
    /// Purges all messages from all queues in the default vhost.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Queue clearing failed.</exception>
    Task ClearAllQueuesAsync(CancellationToken cancellationToken = default);
}
