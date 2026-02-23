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
    }

    // =====================================================================
    // Nested utility — only used by operations below
    // =====================================================================

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

            // Initialize CPU calculator (first sample returns 0.0)
            var cpuCalculator = new ProcessCpuCalculator();
            cpuCalculator.Sample(process);

            using var timer = new PeriodicTimer(samplingInterval);

            while (await timer.WaitForNextTickAsync(ct))
            {
                if (!CollectSample(ctx, process, cpuCalculator, processId, reportProgress, logger))
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
        ProcessCpuCalculator cpuCalculator,
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

            var cpuPercent = cpuCalculator.Sample(process);
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
