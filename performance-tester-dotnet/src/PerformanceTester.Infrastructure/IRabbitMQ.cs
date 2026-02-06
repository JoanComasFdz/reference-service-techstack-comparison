using JoanComasFdz.Result;

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
    /// <returns>Success with Unit, or a Failure describing what went wrong.</returns>
    Task<Result<Unit>> ClearAllQueuesAsync(CancellationToken cancellationToken = default);
}
