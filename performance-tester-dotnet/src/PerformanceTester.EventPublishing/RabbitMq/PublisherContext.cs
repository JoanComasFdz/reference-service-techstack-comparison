using RabbitMQ.Client;

namespace PerformanceTester.EventPublishing.RabbitMq;

/// <summary>
/// Atomic connection state: connection and channel are always created and destroyed together.
/// </summary>
internal sealed record ConnectionState(IConnection Connection, IChannel Channel);

/// <summary>
/// Holds all mutable connection state for the RabbitMQ publisher.
/// Centralizes state per Guideline 05-04 (Context Record Pattern).
/// </summary>
internal sealed record PublisherContext
{
    public SemaphoreSlim ConnectionLock { get; } = new(1, 1);
    public ConnectionState? ActiveConnection { get; set; }
    public bool IsConnected => ActiveConnection != null;
}
