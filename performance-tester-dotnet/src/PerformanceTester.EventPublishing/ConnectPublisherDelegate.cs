namespace PerformanceTester.EventPublishing;

/// <summary>
/// Establishes connection to RabbitMQ.
/// Must be called before <see cref="PublishEventsDelegate"/>.
/// </summary>
public delegate Task ConnectPublisherDelegate(CancellationToken cancellationToken = default);
