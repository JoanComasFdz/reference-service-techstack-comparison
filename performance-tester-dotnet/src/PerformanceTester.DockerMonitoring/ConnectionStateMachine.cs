namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Pure state transition function for Docker streaming connection lifecycle.
/// Given (current state, event) → next state. No side effects.
/// </summary>
internal static class ConnectionStateMachine
{
    /// <summary>
    /// Computes the next connection state given the current state and a stream event.
    /// </summary>
    /// <remarks>
    /// Uses native C# switch expression instead of Dunet's Match() because:
    /// 1. Tuple pattern matching — matches (state, event) pairs simultaneously
    /// 2. 'when' guards — splits same (state, event) pair by runtime condition
    /// 3. Flat transition table — each line is one rule, reads like a state diagram
    /// Match() would require nested calls (state.Match → event.Match) with ternaries
    /// replacing guards, obscuring the state machine structure.
    /// </remarks>
    public static ConnectionState Transition(
        ConnectionState current,
        StreamEvent streamEvent,
        int maxReconnectAttempts,
        TimeSpan maxReconnectDelay) => (current, streamEvent) switch
    {
        // Connecting + valid stats → Connected
        (ConnectionState.Connecting c, StreamEvent.StatsReceived) => new ConnectionState.Connected(c.ContainerId),

        // Connecting + error (under limit) → Disconnected
        (ConnectionState.Connecting c, StreamEvent.Error e) when ReconnectionPolicy.ShouldRetry(c.AttemptNumber + 1, maxReconnectAttempts) =>
            new ConnectionState.Disconnected(
                c.ContainerId,
                c.AttemptNumber + 1,
                c.NextBackoff,
                e.Exception),

        // Connecting + error (at limit) → Failed
        (ConnectionState.Connecting c, StreamEvent.Error e) => new ConnectionState.Failed(c.AttemptNumber + 1, e.Exception),

        // Connected + error → Disconnected (reset to count=1, initial backoff)
        (ConnectionState.Connected c, StreamEvent.Error e) => new ConnectionState.Disconnected(
                c.ContainerId,
                1,
                StreamingConstants.InitialReconnectDelay,
                e.Exception),

        // Disconnected + valid stats → Connected (reset)
        (ConnectionState.Disconnected d, StreamEvent.StatsReceived) =>
            new ConnectionState.Connected(d.ContainerId),

        // Disconnected + error (under limit) → Disconnected with incremented count
        (ConnectionState.Disconnected d, StreamEvent.Error e)
            when ReconnectionPolicy.ShouldRetry(d.ConsecutiveFailures + 1, maxReconnectAttempts) =>
            d with
            {
                ConsecutiveFailures = d.ConsecutiveFailures + 1,
                NextBackoff = ReconnectionPolicy.CalculateBackoff(
                    d.NextBackoff, maxReconnectDelay).NextBackoff,
                LastError = e.Exception
            },

        // Disconnected + error (at limit) → Failed
        (ConnectionState.Disconnected d, StreamEvent.Error e) =>
            new ConnectionState.Failed(d.ConsecutiveFailures + 1, e.Exception),

        // Any state + Cancelled → no-op
        (_, StreamEvent.Cancelled) => current,

        // Default: no-op (safety net)
        _ => current
    };
}
