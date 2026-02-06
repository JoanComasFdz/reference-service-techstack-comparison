using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    private readonly List<ProcessMetrics> _collectedMetrics = [];
    private readonly Lock _metricsLock = new();
    private readonly TaskCompletionSource<int> _processIdSource = new();
    private readonly TaskCompletionSource _firstSampleCollected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // volatile ensures visibility across threads - set in StartMonitoringAsync, read in ExecuteAsync
    private volatile IProgress<ProcessMonitorPhaseInfo>? _progress;
    private int? _processId;
    private string? _cachedProcessName;

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
    public IReadOnlyCollection<ProcessMetrics> GetCollectedMetrics()
    {
        lock (_metricsLock)
        {
            return _collectedMetrics.AsReadOnly();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Process? process = null;
        var cpuCalculator = new ProcessCpuCalculator();

        try
        {
            _logger.LogInformation("ProcessMonitor BackgroundService started, waiting for StartMonitoringAsync() call...");

            var processId = await WaitForProcessIdAsync(stoppingToken);
            if (processId is null)
                return;

            process = InitializeProcess(processId.Value);
            if (process is null)
                return;

            // Initialize CPU calculator (first sample returns 0.0)
            cpuCalculator.Sample(process);

            // Create timer for periodic sampling
            using var timer = new PeriodicTimer(_samplingInterval);

            // Sample until cancellation
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var shouldContinue = CollectSample(process, cpuCalculator, processId.Value);
                if (!shouldContinue)
                    break;
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
            int finalCount;
            lock (_metricsLock)
            {
                finalCount = _collectedMetrics.Count;
            }
            _logger.LogInformation("✓ ProcessMonitor completed: {Count} samples collected", finalCount);

            // Report phase: MonitoringStopped/Completed (use _processId field which may be null if never started)
            if (_processId.HasValue)
            {
                _progress?.Report(ProcessMonitorPhaseInfo.Completed(
                    ProcessMonitorPhase.MonitoringStopped,
                    _processId.Value,
                    sampleCount: finalCount,
                    message: $"Monitoring stopped for PID {_processId.Value}, collected {finalCount} samples"));
            }

            // Dispose process handle
            process?.Dispose();
        }
    }

    /// <summary>
    /// Waits for StartMonitoringAsync() to provide a process ID.
    /// Returns null if cancellation was requested before a process ID was provided.
    /// </summary>
    private async Task<int?> WaitForProcessIdAsync(CancellationToken stoppingToken)
    {
        int processId;
        try
        {
            processId = await _processIdSource.Task.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ProcessMonitor stopped before StartMonitoringAsync() was called");
            return null;
        }

        _logger.LogInformation("ProcessMonitor starting for PID {ProcessId}, sampling every {IntervalMs}ms",
            processId,
            _samplingInterval.TotalMilliseconds);

        return processId;
    }

    /// <summary>
    /// Looks up the process by ID, reads its command line, and caches the meaningful process name.
    /// Returns null if the process was not found, after unblocking the caller and reporting the phase.
    /// </summary>
    private Process? InitializeProcess(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);

            // Extract meaningful process name once (command line doesn't change)
            var commandLine = ReadCommandLine(processId);
            _cachedProcessName = ProcessNameExtractor.ExtractMeaningfulName(process.ProcessName, commandLine);

            _logger.LogInformation("✓ Monitoring process: {ProcessName} (PID: {ProcessId})",
                _cachedProcessName,
                processId);

            return process;
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
            return null;
        }
    }

    /// <summary>
    /// Performs a single sampling iteration: checks process status, collects metrics, and reports progress.
    /// Returns true to continue sampling, false to break out of the sampling loop.
    /// </summary>
    private bool CollectSample(Process process, ProcessCpuCalculator cpuCalculator, int processId)
    {
        try
        {
            // Check if process still exists
            if (process.HasExited)
            {
                _logger.LogWarning("⚠️ Process {ProcessId} has exited", processId);

                // Report phase: ProcessExited/Completed
                int exitedCount;
                lock (_metricsLock)
                {
                    exitedCount = _collectedMetrics.Count;
                }
                _progress?.Report(ProcessMonitorPhaseInfo.Completed(
                    ProcessMonitorPhase.ProcessExited,
                    processId,
                    sampleCount: exitedCount,
                    message: $"Process {processId} has exited"));
                return false;
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
                ProcessName: _cachedProcessName ?? process.ProcessName,
                CpuPercent: Math.Round(cpuPercent, 2),
                MemoryMB: Math.Round(memoryMB, 2),
                ThreadCount: threadCount);

            // Store metrics with lock (maintains chronological order)
            int currentCount;
            lock (_metricsLock)
            {
                _collectedMetrics.Add(metrics);
                currentCount = _collectedMetrics.Count;
            }

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

            return true;
        }
        catch (InvalidOperationException)
        {
            // Process no longer exists (HasExited threw)
            _logger.LogWarning("⚠️ Process {ProcessId} terminated during sampling", processId);

            // Report phase: ProcessExited/Completed
            int terminatedCount;
            lock (_metricsLock)
            {
                terminatedCount = _collectedMetrics.Count;
            }
            _progress?.Report(ProcessMonitorPhaseInfo.Completed(
                ProcessMonitorPhase.ProcessExited,
                processId,
                sampleCount: terminatedCount,
                message: $"Process {processId} terminated during sampling"));
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error sampling process {ProcessId}", processId);
            return true;
        }
    }

    private static string[]? ReadCommandLine(int processId)
    {
        var cmdLinePath = $"/proc/{processId}/cmdline";
        if (!File.Exists(cmdLinePath))
            return null;

        try
        {
            var content = File.ReadAllText(cmdLinePath);
            return content.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (IOException)
        {
            return null; // Process may have exited, permission denied, etc.
        }
    }
}
