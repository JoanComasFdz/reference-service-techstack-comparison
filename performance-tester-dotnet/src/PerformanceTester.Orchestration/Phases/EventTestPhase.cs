using System.Diagnostics;
using PerformanceTester.Functional;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.DockerMonitoring.Monitoring;
using PerformanceTester.EventConsuming;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.ProcessMonitoring;
using PerformanceTester.SystemMonitoring;
using Serilog.Context;
using static PerformanceTester.Functional.Result<PerformanceTester.Orchestration.EventTestPhase.Output, string>;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Phase 1: Measured concurrent publish/consume event throughput test.
/// </summary>
internal static class EventTestPhase
{
    /// <summary>
    /// Clears all collected throughput samples before the measured test starts.
    /// </summary>
    public delegate void ClearSamplesDelegate();

    /// <summary>
    /// Starts process resource monitoring for the given PID.
    /// Returns when the first sample has been collected.
    /// </summary>
    public delegate Task StartProcessMonitoringDelegate(ProcessId processId);

    /// <summary>
    /// Starts system-wide CPU and memory monitoring.
    /// Returns when the first sample has been collected.
    /// </summary>
    public delegate Task StartSystemMonitoringDelegate();

    /// <summary>
    /// Starts all Docker container monitors.
    /// Returns when first samples have been collected from all containers.
    /// </summary>
    public delegate Task StartDockerMonitoringDelegate(ReportDockerMonitorProgressDelegate reportProgress);

    /// <summary>
    /// Success output of the event test phase.
    /// </summary>
    public sealed record Output(
        PublishMetrics PublishMetrics,
        DateTime StartTime,
        DateTime EndTime);

    /// <summary>
    /// Bundles all phase-level and shared delegates needed by <see cref="ExecuteAsync"/>.
    /// </summary>
    public record Dependencies(
        int SystemCpuCount,
        bool SystemIsWsl2,
        ClearSamplesDelegate ClearSamples,
        StartProcessMonitoringDelegate StartProcessMonitoring,
        StartSystemMonitoringDelegate StartSystemMonitoring,
        StartDockerMonitoringDelegate StartDockerMonitoring,
        PhasesToolbox.TrackEventsDelegate TrackEvents,
        PhasesToolbox.PublishEventsDelegate PublishEvents,
        IProgress<ConsumerPhaseInfo> ConsumerProgress);

    /// <summary>
    /// Resolves DI services and composes phase-level delegates into a <see cref="Dependencies"/> bundle.
    /// </summary>
    public static Dependencies BuildDependencies(
        IServiceProvider services,
        PhasesToolbox.TrackEventsDelegate trackEvents,
        PhasesToolbox.PublishEventsDelegate publishEvents,
        TestConfiguration config,
        ReportPhaseProgressDelegate reportProgress,
        CancellationToken ct)
    {
        var systemMonitor = services.GetRequiredService<ISystemMonitor>();
        var metricsCollector = services.GetRequiredService<IMetricsCollector>();
        var processMonitor = services.GetRequiredService<IProcessMonitor>();
        var startDockerMonitoring = services.GetRequiredService<DockerMonitoring.Monitoring.StartDockerMonitoringDelegate>();

        return new Dependencies(
            SystemCpuCount: systemMonitor.CpuCount,
            SystemIsWsl2: systemMonitor.IsWsl2,
            ClearSamples: metricsCollector.ClearSamples,
            StartProcessMonitoring: (pid) => processMonitor.StartMonitoringAsync(pid.Value, cancellationToken: ct),
            StartSystemMonitoring: () => systemMonitor.StartMonitoringAsync(cancellationToken: ct),
            StartDockerMonitoring: (progress) => startDockerMonitoring(progress, ct),
            TrackEvents: trackEvents,
            PublishEvents: publishEvents,
            ConsumerProgress: CreateConsumerProgressCallback(reportProgress, config.EventCount.Value));
    }

    public static async Task<Result<Output, string>> ExecuteAsync(
        ProcessId serviceProcessId,
        TestConfiguration config,
        Dependencies deps,
        ILogger logger)
    {
        using var _ = LogContext.PushProperty("Phase", "EventTest");

        try
        {
            logger.LogInformation(
                "Starting event throughput test with {Count} events",
                config.EventCount);

            // Clear any warmup samples before starting the measured test
            deps.ClearSamples();
            logger.LogDebug("Cleared warmup throughput samples");

            // Start monitoring just before the measured test begins
            // This ensures chart data starts at the same time as the test phases
            logger.LogInformation("Starting process monitoring for PID {ProcessId}...", serviceProcessId);
            await deps.StartProcessMonitoring(serviceProcessId);
            logger.LogInformation("Process monitoring started");

            logger.LogInformation(
                "Starting system-wide monitoring (CPU: {CpuCount} cores, WSL2: {IsWsl2})...",
                deps.SystemCpuCount,
                deps.SystemIsWsl2);
            await deps.StartSystemMonitoring();
            logger.LogInformation("System monitoring started");

            logger.LogInformation("Starting Docker container monitors...");
            await deps.StartDockerMonitoring(_ => { });
            logger.LogInformation("Docker container monitors started (first samples collected)");

            // Capture startTime immediately before launching concurrent publisher/consumer
            // to minimize gap between monitoring start and measurement start
            var startTime = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();

            // CRITICAL: Start publisher and consumer CONCURRENTLY (not sequentially!)
            var consumerTask = deps.TrackEvents(
                config.EventCount,
                config.InactivityTimeout.Value,
                deps.ConsumerProgress);

            var publisherTask = deps.PublishEvents(config.EventCount);

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
                publishMetrics.EventsPerSecond.Value);

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
    /// </summary>
    // CA1859: recommends returning SynchronousProgress<T> (concrete type) instead of IProgress<T>
    // for devirtualization. No benefit here — the return value is immediately passed to TrackEvents,
    // whose parameter type is IProgress<ConsumerPhaseInfo>?, so the interface dispatch remains.
#pragma warning disable CA1859
    private static IProgress<ConsumerPhaseInfo> CreateConsumerProgressCallback(
        ReportPhaseProgressDelegate reportProgress,
        int totalEventCount)
    {
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
                reportProgress(PhaseInfo.Completed(TestPhase.EventTest, "Complete"));
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
                        reportProgress(PhaseInfo.Starting(
                            TestPhase.EventTest,
                            $"Processing: {count}/{totalEventCount} events"));
                    }
                }
            }
        });
    }
}
