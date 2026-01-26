using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace PerformanceTester.ProcessMonitoring;

/// <summary>
/// BackgroundService that monitors process resource usage and stores metrics in memory.
/// Samples CPU, memory, and thread count at regular intervals using PeriodicTimer.
/// Implements IProcessMonitor to provide access to collected metrics.
/// Supports deferred start pattern - process ID is provided via StartMonitoringAsync() after service starts.
/// </summary>
internal sealed class ProcessMonitorService : BackgroundService, IProcessMonitor
{
    private readonly TimeSpan _samplingInterval;
    private readonly ILogger<ProcessMonitorService> _logger;
    private readonly ConcurrentBag<ProcessMetrics> _collectedMetrics = new();
    private readonly TaskCompletionSource<int> _processIdSource = new();
    private readonly TaskCompletionSource _firstSampleCollected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // volatile ensures visibility across threads - set in StartMonitoringAsync, read in ExecuteAsync
    private volatile IProgress<ProcessMonitorPhaseInfo>? _progress;
    private int? _processId;

    /// <inheritdoc />
    public int? ProcessId => _processId;

    public ProcessMonitorService(
        TimeSpan samplingInterval,
        ILogger<ProcessMonitorService> logger)
    {
        if (samplingInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(samplingInterval), samplingInterval, "Sampling interval must be positive");

        _samplingInterval = samplingInterval;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task StartMonitoringAsync(
        int processId,
        IProgress<ProcessMonitorPhaseInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId), processId, "Process ID must be positive");

        if (_processId.HasValue)
            throw new InvalidOperationException($"Monitoring has already been started for process {_processId.Value}");

        _processId = processId;
        _progress = progress;

        // Report phase: MonitoringRequested/Starting
        _progress?.Report(ProcessMonitorPhaseInfo.Starting(
            ProcessMonitorPhase.MonitoringRequested,
            processId,
            message: $"Starting monitoring for process {processId}"));

        _processIdSource.TrySetResult(processId);
        _logger.LogInformation("StartMonitoringAsync called for PID {ProcessId}, waiting for first sample...", processId);

        // Wait for the first sample to be collected
        await _firstSampleCollected.Task.WaitAsync(cancellationToken);

        _logger.LogInformation("First sample collected for PID {ProcessId}", processId);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ProcessMetrics> GetCollectedMetrics() => _collectedMetrics
                                                                            .OrderBy(m => m.Timestamp)
                                                                            .ToList()
                                                                            .AsReadOnly();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Process? process = null;
        var cpuCalculator = new ProcessCpuCalculator();

        try
        {
            _logger.LogInformation("ProcessMonitor BackgroundService started, waiting for StartMonitoringAsync() call...");

            // Wait for StartMonitoring() to be called with process ID
            int processId;
            try
            {
                processId = await _processIdSource.Task.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("ProcessMonitor stopped before StartMonitoringAsync() was called");
                return;
            }

            _logger.LogInformation("ProcessMonitor starting for PID {ProcessId}, sampling every {IntervalMs}ms",
                processId,
                _samplingInterval.TotalMilliseconds);

            // Get process handle
            try
            {
                process = Process.GetProcessById(processId);
                _logger.LogInformation("✓ Monitoring process: {ProcessName} (PID: {ProcessId})",
                    process.ProcessName,
                    processId);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ Process {ProcessId} not found", processId);
                _firstSampleCollected.TrySetResult(); // Unblock caller

                // Report phase: ProcessNotFound/Completed
                _progress?.Report(ProcessMonitorPhaseInfo.Completed(
                    ProcessMonitorPhase.ProcessNotFound,
                    processId,
                    message: $"Process {processId} not found"));
                return;
            }

            // Initialize CPU calculator (first sample returns 0.0)
            cpuCalculator.Sample(process);

            // Create timer for periodic sampling
            using var timer = new PeriodicTimer(_samplingInterval);

            // Sample until cancellation
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    // Check if process still exists
                    if (process.HasExited)
                    {
                        _logger.LogWarning("⚠️ Process {ProcessId} has exited", processId);

                        // Report phase: ProcessExited/Completed
                        _progress?.Report(ProcessMonitorPhaseInfo.Completed(
                            ProcessMonitorPhase.ProcessExited,
                            processId,
                            sampleCount: _collectedMetrics.Count,
                            message: $"Process {processId} has exited"));
                        break;
                    }

                    // Refresh process info (updates memory/threads)
                    process.Refresh();

                    // Sample metrics
                    var cpuPercent = cpuCalculator.Sample(process);
                    var memoryMB = process.WorkingSet64 / 1024.0 / 1024.0;
                    var threadCount = process.Threads.Count;

                    var metrics = new ProcessMetrics(
                        Timestamp: DateTimeOffset.UtcNow,
                        ProcessId: processId,
                        ProcessName: process.ProcessName,
                        CpuPercent: Math.Round(cpuPercent, 2),
                        MemoryMB: Math.Round(memoryMB, 2),
                        ThreadCount: threadCount);

                    // Store metrics directly (thread-safe)
                    _collectedMetrics.Add(metrics);

                    var currentCount = _collectedMetrics.Count;

                    // Signal first sample and report phases
                    var isFirstSample = _firstSampleCollected.TrySetResult();
                    if (isFirstSample)
                    {
                        _progress?.Report(ProcessMonitorPhaseInfo.Completed(
                            ProcessMonitorPhase.FirstSampleCollected,
                            processId,
                            sampleCount: currentCount,
                            message: $"First sample collected for PID {processId}"));
                    }

                    // Always report SampleCollected with current count
                    _progress?.Report(ProcessMonitorPhaseInfo.Completed(
                        ProcessMonitorPhase.SampleCollected,
                        processId,
                        sampleCount: currentCount,
                        message: $"Sample #{currentCount} collected"));
                }
                catch (InvalidOperationException)
                {
                    // Process no longer exists (HasExited threw)
                    _logger.LogWarning("⚠️ Process {ProcessId} terminated during sampling", processId);

                    // Report phase: ProcessExited/Completed
                    _progress?.Report(ProcessMonitorPhaseInfo.Completed(
                        ProcessMonitorPhase.ProcessExited,
                        processId,
                        sampleCount: _collectedMetrics.Count,
                        message: $"Process {processId} terminated during sampling"));
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error sampling process {ProcessId}", processId);
                }
            }

            _logger.LogInformation("ProcessMonitor stopping gracefully");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ProcessMonitor cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ProcessMonitor failed");

            // Ensure caller is unblocked even on unexpected failure
            _firstSampleCollected.TrySetException(ex);
            throw;
        }
        finally
        {
            _logger.LogInformation("✓ ProcessMonitor completed: {Count} samples collected", _collectedMetrics.Count);

            // Report phase: MonitoringStopped/Completed (use _processId field which may be null if never started)
            if (_processId.HasValue)
            {
                _progress?.Report(ProcessMonitorPhaseInfo.Completed(
                    ProcessMonitorPhase.MonitoringStopped,
                    _processId.Value,
                    sampleCount: _collectedMetrics.Count,
                    message: $"Monitoring stopped for PID {_processId.Value}, collected {_collectedMetrics.Count} samples"));
            }

            // Dispose process handle
            process?.Dispose();
        }
    }
}
