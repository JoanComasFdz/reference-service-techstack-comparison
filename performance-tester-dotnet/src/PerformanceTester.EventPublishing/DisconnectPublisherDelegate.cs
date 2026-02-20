namespace PerformanceTester.EventPublishing;

/// <summary>
/// Gracefully disconnects from RabbitMQ.
/// Safe to call multiple times.
/// </summary>
public delegate Task DisconnectPublisherDelegate(CancellationToken cancellationToken = default);
