using PerformanceTester.Functional;

namespace PerformanceTester.Infrastructure.RabbitMQ;

/// <summary>
/// Purges all messages from all queues in the default vhost.
/// Named delegate replacing IRabbitMQ interface (Guideline 12).
/// </summary>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>Success with Unit, or a Failure describing what went wrong.</returns>
public delegate Task<Result<Unit, string>> ClearAllQueues(CancellationToken cancellationToken = default);
