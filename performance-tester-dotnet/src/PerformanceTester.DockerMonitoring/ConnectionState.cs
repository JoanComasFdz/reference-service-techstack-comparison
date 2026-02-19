using Dunet;

namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Explicit state machine for Docker streaming connection lifecycle.
/// Replaces implicit state in volatile fields (_consecutiveFailures, _hasReceivedValidStats,
/// _streamingFailed, _currentBackoffDelay).
/// </summary>
/// <remarks>
/// Uses Dunet [Union] for exhaustive Match() support in single-dispatch contexts.
/// The state machine transition function (<see cref="ConnectionStateMachine.Transition"/>)
/// uses native C# switch expressions instead of Match() because it matches on
/// (state, event) tuples with 'when' guards — a pattern that Match() cannot express.
/// </remarks>
[Union]
internal partial record ConnectionState
{
    /// <summary>
    /// Attempting to connect to Docker stats stream.
    /// </summary>
    partial record Connecting(
        string ContainerId,
        int AttemptNumber,
        TimeSpan NextBackoff);

    /// <summary>
    /// Successfully receiving stats from Docker.
    /// </summary>
    partial record Connected(
        string ContainerId);

    /// <summary>
    /// Connection lost, will retry after waiting <see cref="NextBackoff"/>.
    /// </summary>
    partial record Disconnected(
        string ContainerId,
        int ConsecutiveFailures,
        TimeSpan NextBackoff,
        Exception LastError);

    /// <summary>
    /// Connection failed permanently (max retries exceeded).
    /// </summary>
    partial record Failed(
        int TotalAttempts,
        Exception LastError);
}
