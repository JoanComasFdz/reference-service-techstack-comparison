namespace PerformanceTester.EventPublishing;

/// <summary>
/// Publishes multiple CloudEvents to RabbitMQ as fast as possible.
/// Returns publishing metrics including total duration and throughput.
/// </summary>
public delegate Task<PublishMetrics> PublishEventsDelegate(int count, CancellationToken cancellationToken = default);
