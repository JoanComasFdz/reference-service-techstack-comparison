using PerformanceTester.DockerMonitoring;

namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Helper class for tests to await specific Docker monitoring phase transitions.
/// Aggregates phase events from multiple monitors and provides TaskCompletionSource-based waiting.
/// Aligns with ConsumerPhaseAwaiter pattern from EventConsuming slice.
/// </summary>
/// <remarks>
/// Key difference from ConsumerPhaseAwaiter: This awaiter tracks multiple containers,
/// so all dictionary keys include ContainerName. This is intentional because Docker
/// monitoring tests monitor multiple containers (PostgreSQL and RabbitMQ) simultaneously.
///
/// With streaming mode, phase-based sample counting is removed. Tests should use
/// the DockerMonitorTestExtensions.WaitForSampleCountAsync extension method instead.
/// </remarks>
public sealed class DockerMonitorPhaseAwaiter : IProgress<DockerMonitorPhaseInfo>
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
            // Check if already received
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
    /// Waits for a specific phase (any state) to be reported for the specified container.
    /// Convenience overload that defaults to Completed state.
    /// </summary>
    public Task WaitForPhaseAsync(
        string containerName,
        DockerMonitorPhase phase,
        TimeSpan? timeout = null)
        => WaitForPhaseAsync(containerName, phase, DockerMonitorPhaseState.Completed, timeout);

    /// <summary>
    /// Called by DockerMonitorService via progress?.Report(). Explicit interface implementation
    /// hides this from the public API - callers use Wait* methods instead.
    /// </summary>
    void IProgress<DockerMonitorPhaseInfo>.Report(DockerMonitorPhaseInfo value)
    {
        lock (_lock)
        {
            _receivedPhases.Add(value);

            // Complete phase awaiter
            var phaseKey = (value.ContainerName, value.Phase, value.State);
            if (_phaseAwaiters.TryGetValue(phaseKey, out var phaseTcs))
            {
                phaseTcs.TrySetResult();
                _phaseAwaiters.Remove(phaseKey);
            }
        }
    }
}
