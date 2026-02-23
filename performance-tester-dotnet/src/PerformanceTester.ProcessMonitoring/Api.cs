using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.ProcessMonitoring.ValueObjects;

namespace PerformanceTester.ProcessMonitoring;

// =====================================================================
// Delegates (Guideline 02-05 reading order: delegates first)
// =====================================================================

/// <summary>
/// Reports process monitoring phase changes.
/// Replaces IProgress&lt;ProcessMonitorPhaseInfo&gt; with a named delegate per Guideline 02-02.
/// </summary>
public delegate void ReportProcessMonitorProgressDelegate(ProcessMonitorPhaseInfo phaseInfo);

/// <summary>
/// Starts process resource monitoring for the given PID.
/// Blocks until the first sample has been collected.
/// </summary>
public delegate Task StartProcessMonitoringDelegate(
    ProcessId processId,
    ReportProcessMonitorProgressDelegate reportProgress,
    CancellationToken ct = default);

/// <summary>
/// Returns all collected process metrics in chronological order.
/// Call after test completion (after stopping IHost).
/// </summary>
public delegate IReadOnlyCollection<ProcessMetrics> GetProcessMetricsDelegate();

// =====================================================================
// Phase info — enums and record struct
// =====================================================================

/// <summary>
/// Represents the phases in process monitoring lifecycle.
/// </summary>
public enum ProcessMonitorPhase
{
    /// <summary>Monitoring has been requested via StartMonitoringAsync().</summary>
    MonitoringRequested,

    /// <summary>First metrics sample has been collected.</summary>
    FirstSampleCollected,

    /// <summary>A sample has been collected (reported after each sample).</summary>
    SampleCollected,

    /// <summary>Process was not found during initial lookup.</summary>
    ProcessNotFound,

    /// <summary>Process has exited during monitoring.</summary>
    ProcessExited,

    /// <summary>Monitoring has stopped.</summary>
    MonitoringStopped
}

/// <summary>
/// Represents the state of a phase transition.
/// Aligns with DockerMonitorPhaseState and ConsumerPhaseState from other slices.
/// </summary>
public enum ProcessMonitorPhaseState
{
    /// <summary>Phase is about to start.</summary>
    Starting,

    /// <summary>Phase has completed successfully.</summary>
    Completed,

    /// <summary>Phase failed with an error.</summary>
    Failed
}

/// <summary>
/// Information about a phase transition in process monitoring.
/// Aligns with DockerMonitorPhaseInfo pattern from DockerMonitoring slice.
/// </summary>
/// <param name="Phase">The phase that is transitioning.</param>
/// <param name="State">The state of the transition (Starting, Completed, Failed).</param>
/// <param name="ProcessId">ID of the process being monitored.</param>
/// <param name="SampleCount">Current total sample count.</param>
/// <param name="Message">Optional descriptive message.</param>
/// <param name="Timestamp">When the phase occurred.</param>
public readonly record struct ProcessMonitorPhaseInfo(
    ProcessMonitorPhase Phase,
    ProcessMonitorPhaseState State,
    ProcessId ProcessId,
    SampleCount SampleCount,
    string? Message = null,
    DateTimeOffset? Timestamp = null)
{
    /// <summary>Gets the timestamp, defaulting to now if not specified.</summary>
    public DateTimeOffset TimestampOrNow => Timestamp ?? DateTimeOffset.UtcNow;

    /// <summary>Creates a ProcessMonitorPhaseInfo indicating a phase is starting.</summary>
    public static ProcessMonitorPhaseInfo Starting(
        ProcessMonitorPhase phase,
        ProcessId processId,
        string? message = null) => new(
        phase,
        ProcessMonitorPhaseState.Starting,
        processId,
        SampleCount.FromInt(0),
        message,
        DateTimeOffset.UtcNow);

    /// <summary>Creates a ProcessMonitorPhaseInfo indicating a phase has completed.</summary>
    public static ProcessMonitorPhaseInfo Completed(
        ProcessMonitorPhase phase,
        ProcessId processId,
        string? message = null) => new(
        phase,
        ProcessMonitorPhaseState.Completed,
        processId,
        SampleCount.FromInt(0),
        message,
        DateTimeOffset.UtcNow);

    /// <summary>Creates a ProcessMonitorPhaseInfo indicating a phase has completed with sample count.</summary>
    public static ProcessMonitorPhaseInfo Completed(
        ProcessMonitorPhase phase,
        ProcessId processId,
        SampleCount sampleCount,
        string? message = null) => new(
        phase,
        ProcessMonitorPhaseState.Completed,
        processId,
        sampleCount,
        message,
        DateTimeOffset.UtcNow);

    /// <summary>Creates a ProcessMonitorPhaseInfo indicating a phase has failed.</summary>
    public static ProcessMonitorPhaseInfo Failed(
        ProcessMonitorPhase phase,
        ProcessId processId,
        string? message = null) => new(
        phase,
        ProcessMonitorPhaseState.Failed,
        processId,
        SampleCount.FromInt(0),
        message,
        DateTimeOffset.UtcNow);
}

// =====================================================================
// Data records — public output contracts
// =====================================================================

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

// =====================================================================
// Public utilities
// =====================================================================

/// <summary>
/// Extracts meaningful service names from process command lines.
/// Handles Java JARs, .NET DLLs, native executables, and interpreted languages.
/// </summary>
public static class ProcessNameExtractor
{
    /// <summary>
    /// Extracts a meaningful service name from a process's command line arguments.
    /// Falls back to the base process name if no meaningful name can be extracted.
    /// </summary>
    /// <param name="baseProcessName">The OS-reported process name (e.g., "java", "python3")</param>
    /// <param name="commandLine">The full command line arguments, or null if unavailable</param>
    /// <returns>A meaningful service name for display and reporting</returns>
    public static string ExtractMeaningfulName(string baseProcessName, string[]? commandLine)
    {
        if (commandLine is null || commandLine.Length == 0)
        {
            return baseProcessName;
        }

        // Java processes: look for JAR file or main class
        if (baseProcessName.Equals("java", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractJavaServiceName(commandLine) ?? baseProcessName;
        }

        // .NET processes: look for DLL file (dotnet myapp.dll)
        if (baseProcessName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractDotNetServiceName(commandLine) ?? baseProcessName;
        }

        // Python processes: look for script name
        if (baseProcessName.StartsWith("python", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractPythonServiceName(commandLine) ?? baseProcessName;
        }

        // Node/Bun: look for script name
        if (baseProcessName.Equals("node", StringComparison.OrdinalIgnoreCase) ||
            baseProcessName.Equals("bun", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractNodeServiceName(commandLine) ?? baseProcessName;
        }

        // For native executables, use the executable name from command line if available
        var executableName = Path.GetFileNameWithoutExtension(commandLine[0]);
        return !string.IsNullOrEmpty(executableName) ? executableName : baseProcessName;
    }

    private static string? ExtractJavaServiceName(string[] commandLine)
    {
        for (var i = 0; i < commandLine.Length; i++)
        {
            var arg = commandLine[i];

            // Check for -jar flag followed by JAR path
            if (arg.Equals("-jar", StringComparison.OrdinalIgnoreCase) && i + 1 < commandLine.Length)
            {
                var jarPath = commandLine[i + 1];
                return Path.GetFileNameWithoutExtension(jarPath);
            }

            // Check for JAR file as direct argument (e.g., java myapp.jar)
            if (arg.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && !arg.StartsWith("-"))
            {
                return Path.GetFileNameWithoutExtension(arg);
            }
        }

        // Look for main class (last non-option argument that looks like a class name)
        for (var i = commandLine.Length - 1; i >= 0; i--)
        {
            var arg = commandLine[i];
            if (!arg.StartsWith("-") && !arg.Contains('=') && arg.Contains('.'))
            {
                // Likely a fully qualified class name like com.example.MainClass
                var className = arg.Split('.')[^1]; // Get last segment
                return className;
            }
        }

        return null;
    }

    private static string? ExtractDotNetServiceName(string[] commandLine)
    {
        foreach (var arg in commandLine)
        {
            // Skip flags
            if (arg.StartsWith("-"))
            {
                continue;
            }

            // Found a DLL file
            if (arg.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileNameWithoutExtension(arg);
            }
        }

        return null;
    }

    private static string? ExtractPythonServiceName(string[] commandLine)
    {
        foreach (var arg in commandLine)
        {
            // Skip python executable and flags
            if (arg.StartsWith("-") || arg.Contains("python"))
            {
                continue;
            }

            // Found a script file
            if (arg.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileNameWithoutExtension(arg);
            }
        }

        return null;
    }

    private static string? ExtractNodeServiceName(string[] commandLine)
    {
        foreach (var arg in commandLine)
        {
            // Skip node/bun executable and flags
            if (arg.StartsWith("-"))
            {
                continue;
            }

            // Found a script file
            if (arg.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                arg.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                arg.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileNameWithoutExtension(arg);
            }
        }

        return null;
    }
}
