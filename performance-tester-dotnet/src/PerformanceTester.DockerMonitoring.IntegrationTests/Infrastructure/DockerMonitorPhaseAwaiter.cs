using PerformanceTester.DockerMonitoring.Monitoring;

namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Helper class for tests to await specific Docker monitoring phase transitions.
/// Provides a ReportDockerMonitorProgress-compatible Report property and
/// TaskCompletionSource-based waiting for specific phases.
/// </summary>
public sealed class DockerMonitorPhaseAwaiter
{
    private readonly Dictionary<(string ContainerName, DockerMonitorPhase Phase, DockerMonitorPhaseState State), TaskCompletionSource> _phaseAwaiters = [];
    private readonly List<DockerMonitorPhaseInfo> _receivedPhases = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Gets all phase transitions received so far.
    /// </summary>
    public IReadOnlyList<DockerMonitorPhaseInfo> ReceivedPhases
    {
        get
        {
            lock (_lock)
            {
                return [.. _receivedPhases];
            }
        }
    }

    /// <summary>
    /// The progress reporting delegate. Pass this to StartDockerMonitoring.
    /// </summary>
    public ReportDockerMonitorProgress Report => OnProgressReport;

    /// <summary>
    /// Waits for a specific phase and state to be reported for the specified container.
    /// </summary>
    public async Task WaitForPhaseAsync(
        string containerName,
        DockerMonitorPhase phase,
        DockerMonitorPhaseState state,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        TaskCompletionSource tcs;
        var key = (containerName, phase, state);

        lock (_lock)
        {
            if (_receivedPhases.Any(p => p.ContainerName == containerName && p.Phase == phase && p.State == state))
            {
                return;
            }

            if (!_phaseAwaiters.TryGetValue(key, out tcs!))
            {
                tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _phaseAwaiters[key] = tcs;
            }
        }

        using var cts = new CancellationTokenSource(effectiveTimeout);
        using var registration = cts.Token.Register(() =>
            tcs.TrySetException(new TimeoutException(
                $"Timeout waiting for {containerName} to reach phase {phase}/{state} after {effectiveTimeout.TotalSeconds}s")));

        await tcs.Task;
    }

    /// <summary>
    /// Waits for a specific phase (Completed state) for the specified container.
    /// </summary>
    public Task WaitForPhaseAsync(
        string containerName,
        DockerMonitorPhase phase,
        TimeSpan? timeout = null)
        => WaitForPhaseAsync(containerName, phase, DockerMonitorPhaseState.Completed, timeout);

    private void OnProgressReport(DockerMonitorPhaseInfo value)
    {
        lock (_lock)
        {
            _receivedPhases.Add(value);

            var phaseKey = (value.ContainerName, value.Phase, value.State);
            if (_phaseAwaiters.TryGetValue(phaseKey, out var phaseTcs))
            {
                phaseTcs.TrySetResult();
                _phaseAwaiters.Remove(phaseKey);
            }
        }
    }
}
