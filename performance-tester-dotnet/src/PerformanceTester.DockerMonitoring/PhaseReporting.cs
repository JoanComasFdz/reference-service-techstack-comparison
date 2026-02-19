using PerformanceTester.DockerMonitoring.Connection;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Pure mapping from <see cref="ConnectionState"/> to <see cref="DockerMonitorPhaseInfo"/>.
/// Centralizes phase reporting that was previously scattered across 6+ locations
/// in DockerMonitorService.
/// </summary>
internal static class PhaseReporting
{
    public static DockerMonitorPhaseInfo ToPhaseInfo(
        ConnectionState state,
        NonEmptyString containerName) => state switch
    {
        ConnectionState.Connecting { AttemptNumber.Value: 0 } =>
            DockerMonitorPhaseInfo.Starting(
                DockerMonitorPhase.StreamConnecting,
                containerName.Value,
                message: $"Connecting to {containerName}..."),

        ConnectionState.Connecting c =>
            DockerMonitorPhaseInfo.Starting(
                DockerMonitorPhase.StreamConnecting,
                containerName.Value,
                message: $"Reconnecting to {containerName} (attempt {c.AttemptNumber + 1})..."),

        ConnectionState.Connected =>
            DockerMonitorPhaseInfo.Completed(
                DockerMonitorPhase.StreamConnected,
                containerName.Value,
                message: $"Connected to {containerName}"),

        ConnectionState.Disconnected d =>
            DockerMonitorPhaseInfo.Failed(
                DockerMonitorPhase.StreamDisconnected,
                containerName.Value,
                message: $"Disconnected, retrying in {d.NextBackoff.TotalSeconds:F1}s " +
                         $"(attempt {d.ConsecutiveFailures}/{StreamingConstants.MaxReconnectAttempts})"),

        ConnectionState.Failed f =>
            DockerMonitorPhaseInfo.Failed(
                DockerMonitorPhase.StreamFailed,
                containerName.Value,
                message: $"Connection failed permanently after {f.TotalAttempts} attempts"),

        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
