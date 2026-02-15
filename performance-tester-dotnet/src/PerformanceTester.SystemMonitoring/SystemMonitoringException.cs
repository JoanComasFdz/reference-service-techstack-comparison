namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Exception thrown when system monitoring cannot retrieve required metrics.
/// This indicates a fatal monitoring failure that should stop the performance test.
/// </summary>
public sealed class SystemMonitoringException : Exception
{
    /// <summary>
    /// Gets the platform where the failure occurred.
    /// </summary>
    public string Platform { get; }

    /// <summary>
    /// Gets the type of metric that failed to be collected.
    /// </summary>
    public string MetricType { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="SystemMonitoringException"/> with default values.
    /// </summary>
    /// <remarks>
    /// This constructor exists for serialization scenarios. Prefer using the factory methods
    /// or the full constructor that specifies platform and metric type.
    /// </remarks>
    public SystemMonitoringException()
        : base("A system monitoring error occurred.")
    {
        Platform = "Unknown";
        MetricType = "Unknown";
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SystemMonitoringException"/> with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <remarks>
    /// This constructor exists for serialization scenarios. Prefer using the factory methods
    /// or the full constructor that specifies platform and metric type.
    /// </remarks>
    public SystemMonitoringException(string message)
        : base(message)
    {
        Platform = "Unknown";
        MetricType = "Unknown";
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SystemMonitoringException"/> with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    /// <remarks>
    /// This constructor exists for serialization scenarios. Prefer using the factory methods
    /// or the full constructor that specifies platform and metric type.
    /// </remarks>
    public SystemMonitoringException(string message, Exception innerException)
        : base(message, innerException)
    {
        Platform = "Unknown";
        MetricType = "Unknown";
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SystemMonitoringException"/>.
    /// </summary>
    /// <param name="platform">The platform where the failure occurred.</param>
    /// <param name="metricType">The type of metric that failed.</param>
    /// <param name="message">The error message.</param>
    public SystemMonitoringException(string platform, string metricType, string message)
        : base(message)
    {
        Platform = platform;
        MetricType = metricType;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SystemMonitoringException"/> with an inner exception.
    /// </summary>
    /// <param name="platform">The platform where the failure occurred.</param>
    /// <param name="metricType">The type of metric that failed.</param>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception that caused this failure.</param>
    public SystemMonitoringException(string platform, string metricType, string message, Exception innerException)
        : base(message, innerException)
    {
        Platform = platform;
        MetricType = metricType;
    }

    /// <summary>
    /// Creates an exception for Windows/WSL2 CPU query failure.
    /// </summary>
    public static SystemMonitoringException WindowsCpuQueryFailed(bool isWsl2) => new(
            platform: isWsl2 ? "WSL2" : "Windows",
            metricType: "CPU",
            message: "Failed to query Windows host CPU usage via PowerShell. " +
                     "System monitoring cannot continue without CPU metrics. " +
                     "Please verify PowerShell is accessible and Windows host is responsive.");

    /// <summary>
    /// Creates an exception for Windows/WSL2 memory query failure.
    /// </summary>
    public static SystemMonitoringException WindowsMemoryQueryFailed(bool isWsl2) => new(
            platform: isWsl2 ? "WSL2" : "Windows",
            metricType: "Memory",
            message: "Failed to query Windows host memory usage via PowerShell. " +
                     "System monitoring cannot continue without memory metrics. " +
                     "Please verify PowerShell is accessible and Windows host is responsive.");

    /// <summary>
    /// Creates an exception for Linux /proc/stat CPU read failure.
    /// </summary>
    public static SystemMonitoringException LinuxCpuReadFailed(Exception? innerException = null) => new(
            platform: "Linux",
            metricType: "CPU",
            message: "Failed to read CPU metrics from /proc/stat. " +
                     "System monitoring cannot continue without CPU metrics. " +
                     "Please verify /proc filesystem is accessible.",
            innerException: innerException ?? new InvalidOperationException("CPU reader returned invalid data"));

    /// <summary>
    /// Creates an exception for Linux /proc/meminfo memory read failure.
    /// </summary>
    public static SystemMonitoringException LinuxMemoryReadFailed(Exception? innerException = null) => new(
            platform: "Linux",
            metricType: "Memory",
            message: "Failed to read memory metrics from /proc/meminfo. " +
                     "System monitoring cannot continue without memory metrics. " +
                     "Please verify /proc filesystem is accessible.",
            innerException: innerException ?? new InvalidOperationException("Memory reader returned invalid data"));
}
