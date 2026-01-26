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
/// </remarks>
public sealed class DockerMonitorPhaseAwaiter : IProgress<DockerMonitorPhaseInfo>
{
    private readonly Dictionary<(string ContainerName, int SampleCount), TaskCompletionSource> _sampleAwaiters = [];
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
    /// Gets the current sample count for a container (computed from received phases).
    /// </summary>
    public int GetSampleCount(string containerName)
    {
        lock (_lock)
        {
            return _receivedPhases
                .Where(p => p.ContainerName == containerName &&
                           (p.Phase == DockerMonitorPhase.SampleCollected ||
                            p.Phase == DockerMonitorPhase.FirstSampleCollected))
                .Select(p => p.SampleCount)
                .DefaultIfEmpty(0)
                .Max();
        }
    }

    /// <summary>
    /// Waits until the specified container has collected at least the specified number of samples.
    /// </summary>
    /// <param name="containerName">Name of the container to wait for.</param>
    /// <param name="minimumSampleCount">Minimum number of samples required.</param>
    /// <param name="timeout">Maximum time to wait (default: 30 seconds).</param>
    /// <returns>Task that completes when the sample count is reached.</returns>
    public async Task WaitForSampleCountAsync(
        string containerName,
        int minimumSampleCount,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        TaskCompletionSource tcs;

        lock (_lock)
        {
            // Check if already have enough samples
            if (GetSampleCount(containerName) >= minimumSampleCount)
            {
                return;
            }

            // Create awaiter for this specific count
            var key = (containerName, minimumSampleCount);
            if (!_sampleAwaiters.TryGetValue(key, out tcs!))
            {
                tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _sampleAwaiters[key] = tcs;
            }
        }

        using var cts = new CancellationTokenSource(effectiveTimeout);
        using var registration = cts.Token.Register(() =>
            tcs.TrySetException(new TimeoutException(
                $"Timeout waiting for {containerName} to reach {minimumSampleCount} samples " +
                $"after {effectiveTimeout.TotalSeconds}s. Current count: {GetSampleCount(containerName)}")));

        await tcs.Task;
    }

    /// <summary>
    /// Waits until all specified containers have collected at least the specified number of samples.
    /// </summary>
    public async Task WaitForSampleCountAsync(
        IEnumerable<string> containerNames,
        int minimumSampleCount,
        TimeSpan? timeout = null)
    {
        var tasks = containerNames.Select(name => WaitForSampleCountAsync(name, minimumSampleCount, timeout));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Waits for the first sample to be collected from the specified container.
    /// </summary>
    public Task WaitForFirstSampleAsync(string containerName, TimeSpan? timeout = null)
        => WaitForPhaseAsync(containerName, DockerMonitorPhase.FirstSampleCollected, DockerMonitorPhaseState.Completed, timeout);

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

            // Complete sample count awaiters
            if (value.Phase == DockerMonitorPhase.SampleCollected ||
                value.Phase == DockerMonitorPhase.FirstSampleCollected)
            {
                // Complete any awaiter waiting for this or lower sample count
                var keysToComplete = _sampleAwaiters
                    .Where(kv => kv.Key.ContainerName == value.ContainerName &&
                                 kv.Key.SampleCount <= value.SampleCount)
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var key in keysToComplete)
                {
                    if (_sampleAwaiters.TryGetValue(key, out var tcs))
                    {
                        tcs.TrySetResult();
                        _sampleAwaiters.Remove(key);
                    }
                }
            }

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
