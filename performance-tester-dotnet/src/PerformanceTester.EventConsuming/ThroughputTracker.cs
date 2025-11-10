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
    /// <returns>ThroughputSample if sampling interval elapsed, otherwise null.</returns>
    public ThroughputSample? RecordEvent()
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

            var sample = new ThroughputSample(
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
