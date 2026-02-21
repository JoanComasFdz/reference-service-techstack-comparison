using PerformanceTester.EventConsuming;

namespace PerformanceTester.EventConsuming.IntegrationTests.Infrastructure;

/// <summary>
/// Helper class for tests to await specific consumer phase transitions.
/// Provides TaskCompletionSource-based waiting for consumer phase transitions.
/// </summary>
public sealed class ConsumerPhaseAwaiter
{
    private readonly Dictionary<(ConsumerPhase Phase, ConsumerPhaseState State), TaskCompletionSource> _awaiters = [];
    private readonly Dictionary<int, TaskCompletionSource> _eventCountAwaiters = [];
    private readonly List<ConsumerPhaseInfo> _receivedPhases = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Gets all phase transitions received so far.
    /// </summary>
    public IReadOnlyList<ConsumerPhaseInfo> ReceivedPhases
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
    /// Gets the current received event count (from latest EventReceived phase).
    /// </summary>
    public int CurrentEventCount
    {
        get
        {
            lock (_lock)
            {
                return _receivedPhases
                    .Where(p => p.Phase == ConsumerPhase.EventReceived && p.EventCount.HasValue)
                    .Select(p => p.EventCount!.Value)
                    .DefaultIfEmpty(0)
                    .Max();
            }
        }
    }

    /// <summary>
    /// Creates a task that completes when tracking has started.
    /// Use this instead of Task.Delay to wait for the consumer to be ready for events.
    /// </summary>
    /// <remarks>
    /// NOTE: WaitForConsumerRegisteredAsync is NOT provided because ConnectAsync is called
    /// internally by BackgroundService without a progress parameter. Tests should wait for
    /// TrackingStarted instead, which is reported when StartTrackingEventsAsync is called.
    /// </remarks>
    public Task WaitForTrackingStartedAsync(TimeSpan? timeout = null)
        => WaitForPhaseAsync(ConsumerPhase.TrackingStarted, ConsumerPhaseState.Starting, timeout);

    /// <summary>
    /// Creates a task that completes when at least the specified number of events have been received.
    /// </summary>
    /// <param name="count">Minimum number of events to wait for.</param>
    /// <param name="timeout">Maximum time to wait (default: 60 seconds).</param>
    public async Task WaitForEventCountAsync(int count, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        TaskCompletionSource tcs;

        lock (_lock)
        {
            // Check if already reached
            if (CurrentEventCount >= count)
            {
                return;
            }

            // Create or get existing awaiter for this count
            if (!_eventCountAwaiters.TryGetValue(count, out tcs!))
            {
                tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _eventCountAwaiters[count] = tcs;
            }
        }

        using var cts = new CancellationTokenSource(effectiveTimeout);
        using var registration = cts.Token.Register(() =>
            tcs.TrySetException(new TimeoutException(
                $"Timeout waiting for {count} events after {effectiveTimeout.TotalSeconds}s " +
                $"(current: {CurrentEventCount})")));

        await tcs.Task;
    }

    /// <summary>
    /// Creates a task that completes when the target event count is reached.
    /// </summary>
    public Task WaitForTargetReachedAsync(TimeSpan? timeout = null)
        => WaitForPhaseAsync(ConsumerPhase.TargetReached, ConsumerPhaseState.Completed, timeout);

    /// <summary>
    /// Creates a task that completes when the specified phase transition occurs.
    /// </summary>
    public async Task WaitForPhaseAsync(ConsumerPhase phase, ConsumerPhaseState state, TimeSpan? timeout = null)
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
    /// Called by EventConsumerService via the reportProgress delegate.
    /// </summary>
    public void Report(ConsumerPhaseInfo value)
    {
        lock (_lock)
        {
            _receivedPhases.Add(value);

            // Complete phase awaiters
            var key = (value.Phase, value.State);
            if (_awaiters.TryGetValue(key, out var tcs))
            {
                tcs.TrySetResult();
            }

            // Complete event count awaiters
            if (value.EventCount.HasValue)
            {
                var count = value.EventCount.Value;
                foreach (var kvp in _eventCountAwaiters.Where(k => k.Key <= count).ToList())
                {
                    kvp.Value.TrySetResult();
                    _eventCountAwaiters.Remove(kvp.Key);
                }
            }
        }
    }
}
