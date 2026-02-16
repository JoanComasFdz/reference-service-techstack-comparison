using System.Diagnostics;
using JoanComasFdz.Result;
using Microsoft.Extensions.Logging;
using PerformanceTester.EventConsuming;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure.ValueObjects;
using Serilog.Context;
using static JoanComasFdz.Result.Result<PerformanceTester.Orchestration.EventTestPhase.Output, string>;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Phase 1: Measured concurrent publish/consume event throughput test.
/// </summary>
internal static class EventTestPhase
{
    /// <summary>
    /// Clears all collected throughput samples before the measured test starts.
    /// </summary>
    public delegate void ClearSamples();

    /// <summary>
    /// Starts process resource monitoring for the given PID.
    /// Returns when the first sample has been collected.
    /// </summary>
    public delegate Task StartProcessMonitoring(int processId);

    /// <summary>
    /// Starts system-wide CPU and memory monitoring.
    /// Returns when the first sample has been collected.
    /// </summary>
    public delegate Task StartSystemMonitoring();

    /// <summary>
    /// Starts all Docker container monitors.
    /// Returns when first samples have been collected from all containers.
    /// </summary>
    public delegate Task StartDockerMonitoring();

    /// <summary>
    /// Success output of the event test phase.
    /// </summary>
    public sealed record Output(
        PublishMetrics PublishMetrics,
        DateTime StartTime,
        DateTime EndTime);

    public static async Task<Result<Output, string>> ExecuteAsync(
        TestConfiguration config,
        ProcessId serviceProcessId,
        int systemCpuCount,
        bool systemIsWsl2,
        ClearSamples clearSamples,
        StartProcessMonitoring startProcessMonitoring,
        StartSystemMonitoring startSystemMonitoring,
        StartDockerMonitoring startDockerMonitoring,
        PhasesToolbox.TrackEvents trackEvents,
        PhasesToolbox.PublishEvents publishEvents,
        IProgress<PhaseInfo>? progress,
        ILogger logger)
    {
        using var _ = LogContext.PushProperty("Phase", "EventTest");

        try
        {
            logger.LogInformation(
                "Starting event throughput test with {Count} events",
                config.EventCount);

            // Clear any warmup samples before starting the measured test
            clearSamples();
            logger.LogDebug("Cleared warmup throughput samples");

            // Start monitoring just before the measured test begins
            // This ensures chart data starts at the same time as the test phases
            logger.LogInformation("Starting process monitoring for PID {ProcessId}...", serviceProcessId);
            await startProcessMonitoring(serviceProcessId.Value);
            logger.LogInformation("Process monitoring started");

            logger.LogInformation("Starting system-wide monitoring (CPU: {CpuCount} cores, WSL2: {IsWsl2})...",
                systemCpuCount,
                systemIsWsl2);
            await startSystemMonitoring();
            logger.LogInformation("System monitoring started");

            logger.LogInformation("Starting Docker container monitors...");
            await startDockerMonitoring();
            logger.LogInformation("Docker container monitors started (first samples collected)");

            var consumerProgress = CreateConsumerProgressCallback(progress, config.EventCount.Value);

            // Capture startTime immediately before launching concurrent publisher/consumer
            // to minimize gap between monitoring start and measurement start
            var startTime = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();

            // CRITICAL: Start publisher and consumer CONCURRENTLY (not sequentially!)
            var consumerTask = trackEvents(
                config.EventCount.Value,
                config.InactivityTimeout.Value,
                consumerProgress);

            var publisherTask = publishEvents(config.EventCount.Value);

            // Wait for both to complete
            await Task.WhenAll(consumerTask, publisherTask);
            var publishMetrics = await publisherTask; // Get result from publisher task

            stopwatch.Stop();
            var endTime = DateTime.UtcNow;

            var totalDuration = stopwatch.Elapsed;
            var eventThroughput = config.EventCount.Value / totalDuration.TotalSeconds;

            logger.LogInformation(
                "Event throughput test complete: {Count} events in {Duration:F2}s ({Rate:F2} events/s)",
                config.EventCount,
                totalDuration.TotalSeconds,
                eventThroughput);

            logger.LogInformation(
                "Publishing: {Count} events in {Duration:F2}s ({Rate:F2} events/s)",
                publishMetrics.EventCount,
                publishMetrics.Duration.TotalSeconds,
                publishMetrics.EventsPerSecond);

            return new Success(new Output(publishMetrics, startTime, endTime));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Event test phase failed: {Message}", ex.Message);
            return new Failure(ex.Message);
        }
    }

    /// <summary>
    /// Creates a progress callback that adapts ConsumerPhaseInfo to PhaseInfo with throttling.
    /// Returns null if the parent progress is null.
    /// </summary>
    private static IProgress<ConsumerPhaseInfo>? CreateConsumerProgressCallback(
        IProgress<PhaseInfo>? progress,
        int totalEventCount)
    {
        if (progress == null)
        {
            return null;
        }

        // Use SynchronousProgress to ensure updates happen immediately (not via SynchronizationContext)
        // Throttle by time (200ms) to avoid excessive updates while staying responsive
        // Use lock for thread safety (RabbitMQ events can arrive concurrently)
        var lastProgressTime = DateTime.MinValue;
        var progressThrottleMs = 200;
        var progressLock = new object();

        return new SynchronousProgress<ConsumerPhaseInfo>(info =>
        {
            // When target reached, clear the progress bar immediately (before log appears)
            if (info.Phase == ConsumerPhase.TargetReached)
            {
                progress.Report(PhaseInfo.Completed(TestPhase.EventTest, "Complete"));
                return;
            }

            // Only report on EventReceived with valid count
            if (info.Phase == ConsumerPhase.EventReceived && info.EventCount.HasValue)
            {
                lock (progressLock)
                {
                    var now = DateTime.UtcNow;
                    // Throttle: only report every 200ms
                    if ((now - lastProgressTime).TotalMilliseconds >= progressThrottleMs)
                    {
                        lastProgressTime = now;
                        var count = info.EventCount.Value;
                        // Report via PhaseInfo - adapter converts to TestProgress
                        progress.Report(PhaseInfo.Starting(
                            TestPhase.EventTest,
                            $"Processing: {count}/{totalEventCount} events"));
                    }
                }
            }
        });
    }
}
