using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.ProcessMonitoring.ValueObjects;

namespace PerformanceTester.ProcessMonitoring.Internal;

/// <summary>
/// Internal module (Guideline 05-06 + 02-05): context record → static operations.
/// All internal implementation for process monitoring. Public types are in Api.cs.
/// </summary>
internal static class ProcessMonitorModule
{
    // =====================================================================
    // Context record — all mutable state, no logic (Guideline 05-04)
    // =====================================================================

    /// <summary>
    /// Centralizes all mutable state for process monitoring (Guideline 05-04).
    /// Passed explicitly to static operations — no hidden fields.
    /// </summary>
    internal sealed record MonitorContext
    {
        /// <summary>Thread-safe collection of all sampled metrics.</summary>
        public ConcurrentBag<ProcessMetrics> CollectedMetrics { get; } = new();

        /// <summary>Signal from StartMonitoringAsync → ExecuteAsync (deferred start).</summary>
        public TaskCompletionSource StartSignal { get; } = new();

        /// <summary>Signal from sampling loop → StartMonitoringAsync (first sample collected).</summary>
        public TaskCompletionSource FirstSampleCollected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Cached process name extracted from command line (set once, read many).</summary>
        public string? CachedProcessName { get; set; }

        /// <summary>CPU time from the previous sample (null = not yet initialized).</summary>
        public TimeSpan? PreviousCpuTime { get; set; }

        /// <summary>Wall-clock timestamp of the previous sample (null = not yet initialized).</summary>
        public DateTime? PreviousTimestamp { get; set; }

        /// <summary>Whether StartMonitoringAsync has been called (deferred start guard).</summary>
        public bool Started { get; set; }

        /// <summary>Process ID to monitor (set by StartMonitoringAsync).</summary>
        public ProcessId? ProcessId { get; set; }

        /// <summary>Progress reporting delegate (set by StartMonitoringAsync).</summary>
        public ReportProcessMonitorProgressDelegate? ReportProgress { get; set; }
    }

    /// <summary>
    /// Extracts meaningful service names from process command lines.
    /// Handles Java JARs, .NET DLLs, native executables, and interpreted languages.
    /// </summary>
    internal static class ProcessNameExtractor
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
                    var className = arg.Split('.')[^1];
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

    // =====================================================================
    // Static operations
    // =====================================================================

    /// <summary>
    /// Runs the sampling loop: initializes the process, then samples at regular intervals
    /// until cancellation or process exit.
    /// </summary>
    public static async Task RunSamplingLoopAsync(
        MonitorContext ctx,
        ProcessId processId,
        TimeSpan samplingInterval,
        ReportProcessMonitorProgressDelegate reportProgress,
        ILogger logger,
        CancellationToken ct)
    {
        Process? process = null;

        try
        {
            process = InitializeProcess(ctx, processId, logger);
            if (process is null)
            {
                ctx.FirstSampleCollected.TrySetResult();

                reportProgress(ProcessMonitorPhaseInfo.Completed(
                    ProcessMonitorPhase.ProcessNotFound,
                    processId,
                    message: $"Process {processId} not found"));
                return;
            }

            // Initialize CPU tracking (first sample returns 0.0)
            SampleCpu(ctx, process);

            using var timer = new PeriodicTimer(samplingInterval);

            while (await timer.WaitForNextTickAsync(ct))
            {
                if (!CollectSample(ctx, process, processId, reportProgress, logger))
                {
                    break;
                }
            }

            logger.LogInformation("ProcessMonitor stopping gracefully");
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("ProcessMonitor cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ProcessMonitor failed");
            ctx.FirstSampleCollected.TrySetException(ex);
            throw;
        }
        finally
        {
            var count = SampleCount.FromInt(ctx.CollectedMetrics.Count);
            logger.LogInformation("ProcessMonitor completed: {Count} samples collected", count);

            reportProgress(ProcessMonitorPhaseInfo.Completed(
                ProcessMonitorPhase.MonitoringStopped,
                processId,
                count,
                message: $"Monitoring stopped for PID {processId}, collected {count} samples"));

            process?.Dispose();
        }
    }

    /// <summary>
    /// Looks up the process by ID, reads its command line, and caches the meaningful name.
    /// Returns null if the process was not found.
    /// </summary>
    private static Process? InitializeProcess(
        MonitorContext ctx,
        ProcessId processId,
        ILogger logger)
    {
        try
        {
            var process = Process.GetProcessById(processId.Value);

            var commandLine = ReadCommandLine(processId.Value);
            ctx.CachedProcessName = ProcessNameExtractor.ExtractMeaningfulName(
                process.ProcessName,
                commandLine);

            logger.LogInformation(
                "Monitoring process: {ProcessName} (PID: {ProcessId})",
                ctx.CachedProcessName,
                processId);

            return process;
        }
        catch (ArgumentException ex)
        {
            logger.LogError(ex, "Process {ProcessId} not found", processId);
            return null;
        }
    }

    /// <summary>
    /// Collects a single sample: checks process status, captures metrics, stores them.
    /// Returns true to continue sampling, false to break.
    /// </summary>
    private static bool CollectSample(
        MonitorContext ctx,
        Process process,
        ProcessId processId,
        ReportProcessMonitorProgressDelegate reportProgress,
        ILogger logger)
    {
        try
        {
            if (process.HasExited)
            {
                logger.LogWarning("Process {ProcessId} has exited", processId);

                var exitedCount = SampleCount.FromInt(ctx.CollectedMetrics.Count);
                reportProgress(ProcessMonitorPhaseInfo.Completed(
                    ProcessMonitorPhase.ProcessExited,
                    processId,
                    exitedCount,
                    message: $"Process {processId} has exited"));
                return false;
            }

            process.Refresh();

            var cpuPercent = SampleCpu(ctx, process);
            var memoryMB = process.WorkingSet64 / 1024.0 / 1024.0;
            var threadCount = process.Threads.Count;

            var metrics = new ProcessMetrics(
                Timestamp: DateTimeOffset.UtcNow,
                ProcessId: processId.Value,
                ProcessName: ctx.CachedProcessName ?? process.ProcessName,
                CpuPercent: Math.Round(cpuPercent, 2),
                MemoryMB: Math.Round(memoryMB, 2),
                ThreadCount: threadCount);

            ctx.CollectedMetrics.Add(metrics);
            var currentCount = SampleCount.FromInt(ctx.CollectedMetrics.Count);

            var isFirstSample = ctx.FirstSampleCollected.TrySetResult();
            if (isFirstSample)
            {
                reportProgress(ProcessMonitorPhaseInfo.Completed(
                    ProcessMonitorPhase.FirstSampleCollected,
                    processId,
                    currentCount,
                    message: $"First sample collected for PID {processId}"));
            }

            reportProgress(ProcessMonitorPhaseInfo.Completed(
                ProcessMonitorPhase.SampleCollected,
                processId,
                currentCount,
                message: $"Sample #{currentCount} collected"));

            return true;
        }
        catch (InvalidOperationException)
        {
            logger.LogWarning("Process {ProcessId} terminated during sampling", processId);

            var terminatedCount = SampleCount.FromInt(ctx.CollectedMetrics.Count);
            reportProgress(ProcessMonitorPhaseInfo.Completed(
                ProcessMonitorPhase.ProcessExited,
                processId,
                terminatedCount,
                message: $"Process {processId} terminated during sampling"));
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sampling process {ProcessId}", processId);
            return true;
        }
    }

    /// <summary>
    /// Calculates CPU percentage since last sample, updating state in <paramref name="ctx"/>.
    /// First call initializes state and returns 0.0.
    /// Subsequent calls return CPU usage as percentage of total CPU capacity.
    /// </summary>
    /// <remarks>
    /// <para><strong>Process-Level CPU Calculation</strong></para>
    /// <para>
    /// Measures individual process CPU usage by comparing Process.TotalProcessorTime
    /// against elapsed wall-clock time. Not normalized by core count — matches Python psutil behavior
    /// where 100% = full use of one core, 400% = full use of all 4 cores on a 4-core system.
    /// </para>
    /// <para><strong>Formula:</strong> (CPUTimeDelta / ElapsedTimeDelta) * 100</para>
    /// <para><strong>Not for Docker Containers:</strong></para>
    /// <para>
    /// For Docker container CPU calculation, see <c>StatsProcessing.CalculateCpuPercent</c>
    /// in the PerformanceTester.DockerMonitoring slice. Container CPU calculation uses a different
    /// formula that scales by core count (not normalizes) and compares against system CPU time
    /// (not wall-clock time) to match Docker's cgroup accounting.
    /// </para>
    /// </remarks>
    private static double SampleCpu(MonitorContext ctx, Process process)
    {
        var currentCpuTime = process.TotalProcessorTime;
        var currentTimestamp = DateTime.UtcNow;

        if (ctx.PreviousCpuTime is null || ctx.PreviousTimestamp is null)
        {
            ctx.PreviousCpuTime = currentCpuTime;
            ctx.PreviousTimestamp = currentTimestamp;
            return 0.0;
        }

        var cpuDelta = (currentCpuTime - ctx.PreviousCpuTime.Value).TotalMilliseconds;
        var timeDelta = (currentTimestamp - ctx.PreviousTimestamp.Value).TotalMilliseconds;

        ctx.PreviousCpuTime = currentCpuTime;
        ctx.PreviousTimestamp = currentTimestamp;

        if (timeDelta <= 0)
        {
            return 0.0;
        }

        var cpuPercent = (cpuDelta / timeDelta) * 100.0;

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

    private static string[]? ReadCommandLine(int processId)
    {
        var cmdLinePath = $"/proc/{processId}/cmdline";
        if (!File.Exists(cmdLinePath))
        {
            return null;
        }

        try
        {
            var content = File.ReadAllText(cmdLinePath);
            return content.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
