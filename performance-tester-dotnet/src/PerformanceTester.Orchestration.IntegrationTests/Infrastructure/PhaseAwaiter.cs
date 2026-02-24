using PerformanceTester.Orchestration;

namespace PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

/// <summary>
/// Helper class for tests to await specific phase transitions.
/// Wraps IProgress&lt;PhaseInfo&gt; and provides TaskCompletionSource-based waiting.
/// </summary>
public sealed class PhaseAwaiter
{
    private readonly Dictionary<(TestPhase Phase, PhaseState State), TaskCompletionSource> _awaiters = new();
    private readonly List<PhaseInfo> _receivedPhases = new();
    private readonly Lock _lock = new();

    /// <summary>
    /// Gets all phase transitions received so far.
    /// </summary>
    public IReadOnlyList<PhaseInfo> ReceivedPhases
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
    /// Creates a task that completes when the specified phase starts.
    /// </summary>
    /// <param name="phase">The phase to wait for.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <returns>A task that completes when the phase starts.</returns>
    public Task WaitForPhaseStartAsync(TestPhase phase, TimeSpan? timeout = null)
        => WaitForPhaseAsync(phase, PhaseState.Starting, timeout);

    /// <summary>
    /// Creates a task that completes when the specified phase completes.
    /// </summary>
    /// <param name="phase">The phase to wait for.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <returns>A task that completes when the phase completes.</returns>
    public Task WaitForPhaseCompleteAsync(TestPhase phase, TimeSpan? timeout = null)
        => WaitForPhaseAsync(phase, PhaseState.Completed, timeout);

    /// <summary>
    /// Creates a task that completes when the specified phase transition occurs.
    /// </summary>
    /// <param name="phase">The phase to wait for.</param>
    /// <param name="state">The state to wait for (Starting, Completed, Failed).</param>
    /// <param name="timeout">Maximum time to wait (default: 60 seconds).</param>
    /// <returns>A task that completes when the transition occurs.</returns>
    public async Task WaitForPhaseAsync(TestPhase phase, PhaseState state, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        var key = (phase, state);
        TaskCompletionSource tcs;

        lock (_lock)
        {
            // Check if already received
            if (_receivedPhases.Any(p => p.Phase == phase && p.State == state))
            {
                return;
            }

            // Create or get existing awaiter
            if (!_awaiters.TryGetValue(key, out tcs!))
            {
                tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _awaiters[key] = tcs;
            }
        }

        using var cts = new CancellationTokenSource(effectiveTimeout);
        using var registration = cts.Token.Register(() =>
            tcs.TrySetException(new TimeoutException(
                $"Timeout waiting for phase {phase} to reach state {state} after {effectiveTimeout.TotalSeconds}s")));

        await tcs.Task;
    }

    /// <summary>
    /// Called by TestOrchestrationModule via the ReportPhaseProgress delegate.
    /// </summary>
    public void Report(PhaseInfo value)
    {
        lock (_lock)
        {
            _receivedPhases.Add(value);

            var key = (value.Phase, value.State);
            if (_awaiters.TryGetValue(key, out var tcs))
            {
                tcs.TrySetResult();
            }
        }
    }
}
