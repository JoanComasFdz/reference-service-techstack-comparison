using System.Diagnostics;

namespace PerformanceTester.ProcessMonitoring;

/// <summary>
/// Calculates CPU usage percentage from Process.TotalProcessorTime deltas.
/// Maintains state between samples for accurate calculation.
/// Thread-safe for single writer (use lock if multiple threads call Sample).
/// </summary>
/// <remarks>
/// <para><strong>Process-Level CPU Calculation</strong></para>
/// <para>
/// This calculator measures individual process CPU usage by comparing Process.TotalProcessorTime
/// against elapsed wall-clock time, normalized by core count to show per-core average usage.
/// </para>
/// <para><strong>Not for Docker Containers:</strong></para>
/// <para>
/// For Docker container CPU calculation, see <c>StatsProcessing.CalculateCpuPercent</c>
/// in the PerformanceTester.DockerMonitoring slice. Container CPU calculation uses a different
/// formula that scales by core count (not normalizes) and compares against system CPU time
/// (not wall-clock time) to match Docker's cgroup accounting.
/// </para>
/// </remarks>
internal sealed class ProcessCpuCalculator
{
    private TimeSpan _previousCpuTime;
    private DateTime _previousTimestamp;
    private bool _initialized;

    /// <summary>
    /// Calculates CPU percentage since last sample.
    /// First call initializes state and returns 0.0.
    /// Subsequent calls return CPU usage as percentage of total CPU capacity.
    /// </summary>
    /// <param name="process">Process to sample.</param>
    /// <returns>CPU percentage (0 to 100 * core_count, representing total CPU capacity).</returns>
    /// <remarks>
    /// <para><strong>Formula:</strong> (CPUTimeDelta / ElapsedTimeDelta) * 100</para>
    /// <para>
    /// Example on a 4-core system: If 200ms of CPU time was used in 1000ms elapsed:
    /// (200ms / 1000ms) * 100 = 20% of total capacity (max 400% on 4-core)
    /// </para>
    /// <para><strong>Matches Python psutil behavior:</strong></para>
    /// <para>
    /// psutil.Process().cpu_percent() returns CPU utilization as percentage of
    /// total system CPU capacity, where 100% = full use of one core, 400% = full
    /// use of all 4 cores on a 4-core system.
    /// </para>
    /// </remarks>
    public double Sample(Process process)
    {
        var currentCpuTime = process.TotalProcessorTime;
        var currentTimestamp = DateTime.UtcNow;

        if (!_initialized)
        {
            _previousCpuTime = currentCpuTime;
            _previousTimestamp = currentTimestamp;
            _initialized = true;
            return 0.0; // First sample, no delta to calculate
        }

        var cpuDelta = (currentCpuTime - _previousCpuTime).TotalMilliseconds;
        var timeDelta = (currentTimestamp - _previousTimestamp).TotalMilliseconds;

        // Update for next iteration
        _previousCpuTime = currentCpuTime;
        _previousTimestamp = currentTimestamp;

        // Avoid division by zero
        if (timeDelta <= 0)
        {
            return 0.0;
        }

        // Calculate percentage (total CPU capacity, matches Python psutil)
        var cpuPercent = (cpuDelta / timeDelta) * 100.0;

        // Clamp to reasonable range (0 to 100 * cores)
        var maxPercent = Environment.ProcessorCount * 100.0;
        if (cpuPercent < 0)
        {
            cpuPercent = 0;
        }

        if (cpuPercent > maxPercent)
        {
            cpuPercent = maxPercent;
        }

        return cpuPercent;
    }

    /// <summary>
    /// Resets the calculator state.
    /// Next Sample() call will re-initialize.
    /// </summary>
    public void Reset()
    {
        _initialized = false;
    }
}
