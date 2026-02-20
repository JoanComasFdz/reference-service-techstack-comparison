using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.EventPublishing;

/// <summary>
/// Establishes connection to RabbitMQ.
/// Must be called before <see cref="PublishEventsDelegate"/>.
/// </summary>
public delegate Task ConnectPublisherDelegate(CancellationToken cancellationToken = default);

/// <summary>
/// Gracefully disconnects from RabbitMQ.
/// Safe to call multiple times.
/// </summary>
public delegate Task DisconnectPublisherDelegate(CancellationToken cancellationToken = default);

/// <summary>
/// Publishes multiple CloudEvents to RabbitMQ as fast as possible.
/// Returns publishing metrics including total duration and throughput.
/// </summary>
public delegate Task<PublishMetrics> PublishEventsDelegate(EventCount count, CancellationToken cancellationToken = default);
