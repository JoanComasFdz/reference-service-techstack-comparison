using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.ProcessMonitoring.ValueObjects;

namespace PerformanceTester.ProcessMonitoring.Monitoring;

/// <summary>
/// Pure static operations for process monitoring.
/// All state access goes through <see cref="MonitorContext"/> parameter (Guideline 2).
/// Delegates to <see cref="ProcessCpuCalculator"/> and <see cref="ProcessNameExtractor"/> for calculations.
/// </summary>
internal static class MonitoringOperations
{
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
