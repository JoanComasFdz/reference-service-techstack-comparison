using PerformanceTester.ProcessMonitoring.Monitoring;

namespace PerformanceTester.ProcessMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Helper class for tests to await specific process monitoring phase transitions.
/// Provides TaskCompletionSource-based waiting for deterministic test synchronization.
/// Aligns with DockerMonitorPhaseAwaiter pattern.
/// </summary>
/// <remarks>
/// Key difference from DockerMonitorPhaseAwaiter: This awaiter tracks a single process,
/// so dictionary keys do not include ProcessId. This simplifies the implementation since
/// process monitoring tests only monitor one process at a time.
/// </remarks>
public sealed class ProcessMonitorPhaseAwaiter
{
    private readonly Dictionary<int, TaskCompletionSource> _sampleAwaiters = [];
    private readonly Dictionary<(ProcessMonitorPhase Phase, ProcessMonitorPhaseState State), TaskCompletionSource> _phaseAwaiters = [];
    private readonly List<ProcessMonitorPhaseInfo> _receivedPhases = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Gets all phase transitions received so far.
    /// </summary>
    public IReadOnlyList<ProcessMonitorPhaseInfo> ReceivedPhases
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
    /// Gets the current sample count (computed from received phases).
    /// </summary>
    public int SampleCount
    {
        get
        {
            lock (_lock)
            {
                return _receivedPhases
                    .Where(p => p.Phase == ProcessMonitorPhase.SampleCollected ||
                               p.Phase == ProcessMonitorPhase.FirstSampleCollected)
                    .Select(p => p.SampleCount.Value)
                    .DefaultIfEmpty(0)
                    .Max();
            }
        }
    }

    /// <summary>
    /// Waits until at least the specified number of samples have been collected.
    /// </summary>
    /// <param name="minimumSampleCount">Minimum number of samples required.</param>
    /// <param name="timeout">Maximum time to wait (default: 30 seconds).</param>
    /// <returns>Task that completes when the sample count is reached.</returns>
    public async Task WaitForSampleCountAsync(
        int minimumSampleCount,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        TaskCompletionSource tcs;

        lock (_lock)
        {
            // Check if already have enough samples
            if (SampleCount >= minimumSampleCount)
            {
                return;
            }

            // Create awaiter for this specific count
            if (!_sampleAwaiters.TryGetValue(minimumSampleCount, out tcs!))
            {
                tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _sampleAwaiters[minimumSampleCount] = tcs;
            }
        }

        using var cts = new CancellationTokenSource(effectiveTimeout);
        using var registration = cts.Token.Register(() =>
            tcs.TrySetException(new TimeoutException(
                $"Timeout waiting for {minimumSampleCount} samples " +
                $"after {effectiveTimeout.TotalSeconds}s. Current count: {SampleCount}")));

        await tcs.Task;
    }

    /// <summary>
    /// Waits for the first sample to be collected.
    /// </summary>
    public Task WaitForFirstSampleAsync(TimeSpan? timeout = null)
        => WaitForPhaseAsync(ProcessMonitorPhase.FirstSampleCollected, ProcessMonitorPhaseState.Completed, timeout);

    /// <summary>
    /// Waits for monitoring to stop.
    /// </summary>
    public Task WaitForMonitoringStoppedAsync(TimeSpan? timeout = null)
        => WaitForPhaseAsync(ProcessMonitorPhase.MonitoringStopped, ProcessMonitorPhaseState.Completed, timeout);

    /// <summary>
    /// Waits for a specific phase and state to be reported.
    /// </summary>
    public async Task WaitForPhaseAsync(
        ProcessMonitorPhase phase,
        ProcessMonitorPhaseState state,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        TaskCompletionSource tcs;
        var key = (phase, state);

        lock (_lock)
        {
            // Check if already received
            if (_receivedPhases.Any(p => p.Phase == phase && p.State == state))
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
                $"Timeout waiting for phase {phase}/{state} after {effectiveTimeout.TotalSeconds}s")));

        await tcs.Task;
    }

    /// <summary>
    /// Waits for a specific phase (defaults to Completed state).
    /// </summary>
    public Task WaitForPhaseAsync(
        ProcessMonitorPhase phase,
        TimeSpan? timeout = null)
        => WaitForPhaseAsync(phase, ProcessMonitorPhaseState.Completed, timeout);

    /// <summary>
    /// Reports a phase transition. Matches <see cref="ReportProcessMonitorProgressDelegate"/> signature
    /// so it can be passed directly as the delegate target.
    /// </summary>
    public void Report(ProcessMonitorPhaseInfo value)
    {
        lock (_lock)
        {
            _receivedPhases.Add(value);

            // Complete sample count awaiters
            if (value.Phase == ProcessMonitorPhase.SampleCollected ||
                value.Phase == ProcessMonitorPhase.FirstSampleCollected)
            {
                // Complete any awaiter waiting for this or lower sample count
                var keysToComplete = _sampleAwaiters
                    .Where(kv => kv.Key <= value.SampleCount.Value)
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
            var phaseKey = (value.Phase, value.State);
            if (_phaseAwaiters.TryGetValue(phaseKey, out var phaseTcs))
            {
                phaseTcs.TrySetResult();
                _phaseAwaiters.Remove(phaseKey);
            }
        }
    }
}
