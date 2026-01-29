namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Reads system-wide CPU usage from /proc/stat.
/// Calculates CPU percentage from deltas between samples (non-blocking).
/// </summary>
/// <remarks>
/// /proc/stat format:
/// cpu  user nice system idle iowait irq softirq steal guest guest_nice
///
/// Formula:
/// total = user + nice + system + idle + iowait + irq + softirq + steal
/// active = total - idle - iowait
/// CPU% = 100 * (activeDelta / totalDelta)
/// </remarks>
internal sealed class ProcStatReader
{
    private const string ProcStatPath = "/proc/stat";

    private long _previousTotal;
    private long _previousIdle;
    private bool _initialized;

    /// <summary>
    /// Samples current CPU usage percentage.
    /// First call initializes state and returns 0.0.
    /// Subsequent calls return CPU usage as percentage (0-100).
    /// </summary>
    /// <returns>CPU percentage (0-100), or 0.0 on first call or error.</returns>
    public double Sample()
    {
        try
        {
            var (total, idle) = ReadCpuTimes();

            if (!_initialized)
            {
                _previousTotal = total;
                _previousIdle = idle;
                _initialized = true;
                return 0.0; // First sample, no delta to calculate
            }

            var totalDelta = total - _previousTotal;
            var idleDelta = idle - _previousIdle;

            _previousTotal = total;
            _previousIdle = idle;

            if (totalDelta <= 0)
            {
                return 0.0;
            }

            var cpuPercent = 100.0 * (totalDelta - idleDelta) / totalDelta;

            // Clamp to valid range
            return Math.Clamp(cpuPercent, 0.0, 100.0);
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>
    /// Resets the reader state. Next Sample() call will re-initialize.
    /// </summary>
    public void Reset()
    {
        _initialized = false;
    }

    /// <summary>
    /// Reads CPU times from /proc/stat.
    /// </summary>
    /// <returns>Tuple of (total_time, idle_time) in jiffies.</returns>
    private static (long Total, long Idle) ReadCpuTimes()
    {
        // Read first line: cpu  user nice system idle iowait irq softirq steal [guest guest_nice]
        var lines = File.ReadAllLines(ProcStatPath);
        var cpuLine = lines.FirstOrDefault(l => l.StartsWith("cpu "))
            ?? throw new InvalidOperationException("/proc/stat missing cpu line");

        // Split and parse values (skip "cpu" label)
        var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5)
        {
            throw new InvalidOperationException($"Unexpected /proc/stat format: {cpuLine}");
        }

        // Parse values (indices: 0=cpu, 1=user, 2=nice, 3=system, 4=idle, 5=iowait, 6=irq, 7=softirq, 8=steal)
        var user = long.Parse(parts[1]);
        var nice = long.Parse(parts[2]);
        var system = long.Parse(parts[3]);
        var idle = long.Parse(parts[4]);
        var iowait = parts.Length > 5 ? long.Parse(parts[5]) : 0;
        var irq = parts.Length > 6 ? long.Parse(parts[6]) : 0;
        var softirq = parts.Length > 7 ? long.Parse(parts[7]) : 0;
        var steal = parts.Length > 8 ? long.Parse(parts[8]) : 0;

        var total = user + nice + system + idle + iowait + irq + softirq + steal;
        var idleTotal = idle + iowait;

        return (total, idleTotal);
    }
}
