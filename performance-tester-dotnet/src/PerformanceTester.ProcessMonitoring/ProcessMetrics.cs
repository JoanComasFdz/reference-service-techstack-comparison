namespace PerformanceTester.ProcessMonitoring;

/// <summary>
/// Represents process resource metrics at a specific point in time.
/// This model is owned by the ProcessMonitoring slice (producer-owned contract).
/// </summary>
/// <param name="Timestamp">When this sample was captured (UTC).</param>
/// <param name="ProcessId">Process ID being monitored.</param>
/// <param name="ProcessName">Name of the process.</param>
/// <param name="CpuPercent">CPU usage percentage (0-100 per core, can exceed 100 on multi-core systems).</param>
/// <param name="MemoryMB">Memory usage in megabytes (Working Set).</param>
/// <param name="ThreadCount">Number of threads in the process.</param>
public record ProcessMetrics(
    DateTimeOffset Timestamp,
    int ProcessId,
    string ProcessName,
    double CpuPercent,
    double MemoryMB,
    int ThreadCount);
