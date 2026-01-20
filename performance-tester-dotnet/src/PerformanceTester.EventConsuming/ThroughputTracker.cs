using System.Diagnostics;

namespace PerformanceTester.EventConsuming;

/// <summary>
/// Calculates throughput rates from event timestamps.
/// Samples throughput every 500ms based on events received in time window.
/// Thread-safe for concurrent event recording.
/// </summary>
internal sealed class ThroughputTracker
{
    private readonly TimeSpan _samplingInterval = TimeSpan.FromMilliseconds(500);
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly object _lock = new();

    private int _totalEventCount;
    private DateTimeOffset _lastSampleTime;
    private int _lastSampleCount;

    public ThroughputTracker()
    {
        _lastSampleTime = DateTimeOffset.UtcNow;
        _lastSampleCount = 0;
    }

    /// <summary>
    /// Records that an event was received and calculates throughput sample if interval elapsed.
    /// Thread-safe - can be called from consumer callback.
    /// </summary>
    /// <returns>EventThroughputSample if sampling interval elapsed, otherwise null.</returns>
    public EventThroughputSample? RecordEvent()
    {
        lock (_lock)
        {
            _totalEventCount++;
            var now = DateTimeOffset.UtcNow;
            var elapsed = now - _lastSampleTime;

            // Only sample if interval has elapsed
            if (elapsed < _samplingInterval)
            {
                return null;
            }

            // Calculate throughput rate
            var eventsInPeriod = _totalEventCount - _lastSampleCount;
            var eventsPerSecond = eventsInPeriod / elapsed.TotalSeconds;

            var sample = new EventThroughputSample(
                Timestamp: now,
                ThroughputEventsPerSecond: eventsPerSecond,
                CumulativeEventCount: _totalEventCount);

            // Update tracking state
            _lastSampleTime = now;
            _lastSampleCount = _totalEventCount;

            return sample;
        }
    }

    /// <summary>
    /// Gets a final throughput sample with all remaining events since last sample.
    /// Call this during shutdown to capture any events not yet sampled.
    /// Returns null if no new events since last sample.
    /// </summary>
    public EventThroughputSample? GetFinalSample()
    {
        lock (_lock)
        {
            // No new events since last sample
            if (_totalEventCount == _lastSampleCount)
            {
                return null;
            }

            var now = DateTimeOffset.UtcNow;
            var elapsed = now - _lastSampleTime;

            // Avoid division by zero for very fast completion
            var seconds = Math.Max(elapsed.TotalSeconds, 0.001);
            var eventsInPeriod = _totalEventCount - _lastSampleCount;
            var eventsPerSecond = eventsInPeriod / seconds;

            // Update tracking state (consistent with RecordEvent)
            _lastSampleTime = now;
            _lastSampleCount = _totalEventCount;

            return new EventThroughputSample(
                Timestamp: now,
                ThroughputEventsPerSecond: eventsPerSecond,
                CumulativeEventCount: _totalEventCount);
        }
    }

    /// <summary>
    /// Gets the current total event count (thread-safe).
    /// </summary>
    public int TotalEventCount
    {
        get
        {
            lock (_lock)
            {
                return _totalEventCount;
            }
        }
    }
}
