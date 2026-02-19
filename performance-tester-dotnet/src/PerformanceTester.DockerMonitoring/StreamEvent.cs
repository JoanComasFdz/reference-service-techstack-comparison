using Dunet;

namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Events that can occur during a Docker stats streaming session.
/// Used as input to <see cref="ConnectionStateMachine.Transition"/>.
/// </summary>
[Union]
internal partial record StreamEvent
{
    /// <summary>Valid stats were received from the stream.</summary>
    partial record StatsReceived;

    /// <summary>An error occurred in the stream.</summary>
    partial record Error(Exception Exception);

    /// <summary>The stream was cancelled (normal shutdown).</summary>
    partial record Cancelled;
}
