using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// BackgroundService that monitors system-wide CPU and memory usage.
/// Implements deferred start pattern: waits for StartMonitoringAsync() call.
///
/// Platform behavior:
/// - Windows native or WSL2: Queries Windows host for both CPU and memory via PowerShell
/// - Native Linux: Uses /proc/stat for CPU and /proc/meminfo for memory
/// </summary>
internal sealed class SystemMonitorService : BackgroundService, ISystemMonitor
{
    private readonly TimeSpan _samplingInterval;
    private readonly ILogger<SystemMonitorService> _logger;
    private readonly List<SystemMetrics> _collectedMetrics = [];
    private readonly Lock _metricsLock = new();
    private readonly TaskCompletionSource _startRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstSampleCollected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private volatile IProgress<SystemMonitorPhaseInfo>? _progress;
    private DateTimeOffset _monitoringStartTime;

    // Lazy-initialized readers (created on first use to avoid startup cost)
    private ProcStatReader? _cpuReader;
    private ProcMeminfoReader? _memoryReader;

    /// <inheritdoc />
    public bool IsWsl2 { get; }

    /// <inheritdoc />
    public int CpuCount { get; }

    /// <summary>
    /// Gets whether this system should use Windows queries (native Windows or WSL2).
    /// </summary>
    private bool UseWindowsQueries { get; }

    public SystemMonitorService(
        TimeSpan samplingInterval,
        ILogger<SystemMonitorService> logger)
    {
        if (samplingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(samplingInterval), samplingInterval, "Sampling interval must be positive");
        }

        _samplingInterval = samplingInterval;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Detect environment at construction
        IsWsl2 = Wsl2Detector.IsWsl2();
        CpuCount = Environment.ProcessorCount;

        // Container-first detection: containers always use /proc for accurate container metrics
        if (ContainerDetector.IsContainer)
        {
            UseWindowsQueries = false;
            _logger.LogInformation(
                "Container environment detected - using /proc monitoring (container metrics). Kernel: {Kernel}",
                IsWsl2 ? "WSL2" : "Linux");
        }
        else if (OperatingSystem.IsWindows())
        {
            UseWindowsQueries = true;
            _logger.LogInformation("Windows detected - using PowerShell monitoring");
        }
        else if (IsWsl2 && PowerShellHelper.IsAvailable)
        {
            UseWindowsQueries = true;
            var psPath = PowerShellHelper.GetPowerShellPath();
            _logger.LogInformation(
                "WSL2 detected with PowerShell access - using Windows host metrics via {PowerShellPath}", psPath);
        }
        else
        {
            UseWindowsQueries = false;
            if (IsWsl2)
            {
                _logger.LogInformation(
                    "WSL2 kernel detected but PowerShell not accessible - using /proc monitoring");
            }
            else
            {
                _logger.LogInformation("Native Linux detected - using /proc monitoring");
            }
        }
    }

    /// <inheritdoc />
    public async Task StartMonitoringAsync(
        IProgress<SystemMonitorPhaseInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _progress = progress;

        _progress?.Report(SystemMonitorPhaseInfo.Starting(
            SystemMonitorPhase.MonitoringRequested,
            message: "Starting system-wide monitoring"));

        _startRequested.TrySetResult();
        _logger.LogInformation("StartMonitoringAsync called, waiting for first sample...");

        // Wait for the first sample to be collected
        await _firstSampleCollected.Task.WaitAsync(cancellationToken);

        _logger.LogInformation("First system sample collected");
    }

    /// <inheritdoc />
    public IReadOnlyCollection<SystemMetrics> GetCollectedMetrics()
    {
        lock (_metricsLock)
        {
            return _collectedMetrics.AsReadOnly();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("SystemMonitor BackgroundService started, waiting for StartMonitoringAsync() call...");

            // Wait for StartMonitoringAsync() to be called
            try
            {
                await _startRequested.Task.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("SystemMonitor stopped before StartMonitoringAsync() was called");
                return;
            }

            _logger.LogInformation("SystemMonitor starting, sampling every {IntervalMs}ms, CPU cores: {CpuCount}, UseWindowsQueries: {UseWindows}",
                _samplingInterval.TotalMilliseconds,
                CpuCount,
                UseWindowsQueries);

            // Initialize Linux readers for native Linux (WSL2 uses Windows queries via PowerShell)
            if (OperatingSystem.IsLinux())
            {
                _cpuReader = new ProcStatReader();
                _memoryReader = new ProcMeminfoReader();

                // Initialize CPU reader (first sample returns 0.0)
                _cpuReader.Sample();
            }

            _monitoringStartTime = DateTimeOffset.UtcNow;

            // Create timer for periodic sampling
            using var timer = new PeriodicTimer(_samplingInterval);

            // Sample until cancellation
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var metrics = await SampleMetricsAsync(stoppingToken);
                    int currentCount;
                    lock (_metricsLock)
                    {
                        _collectedMetrics.Add(metrics);
                        currentCount = _collectedMetrics.Count;
                    }

                    // Signal first sample
                    var isFirstSample = _firstSampleCollected.TrySetResult();
                    if (isFirstSample)
                    {
                        _progress?.Report(SystemMonitorPhaseInfo.Completed(
                            SystemMonitorPhase.FirstSampleCollected,
                            sampleCount: currentCount,
                            message: "First system sample collected"));
                    }

                    // Report progress every 10 samples
                    if (currentCount % 10 == 0)
                    {
                        _logger.LogDebug("Collected {Count} system metrics", currentCount);
                        _progress?.Report(SystemMonitorPhaseInfo.Completed(
                            SystemMonitorPhase.SampleCollected,
                            sampleCount: currentCount,
                            message: $"Collected {currentCount} samples"));
                    }
                }
                catch (SystemMonitoringException ex)
                {
                    // Fatal monitoring error - stop with clear message
                    _logger.LogError("SYSTEM MONITORING FAILED on {Platform}: {Message}", ex.Platform, ex.Message);
                    _logger.LogError("Cannot continue without {MetricType} metrics. Stopping system monitor.", ex.MetricType);

                    int sampleCount;
                    lock (_metricsLock)
                    {
                        sampleCount = _collectedMetrics.Count;
                    }
                    _progress?.Report(SystemMonitorPhaseInfo.Failed(
                        SystemMonitorPhase.MonitoringStopped,
                        sampleCount: sampleCount,
                        message: $"FATAL: {ex.Platform} {ex.MetricType} monitoring failed - {ex.Message}"));

                    _firstSampleCollected.TrySetException(ex);
                    throw; // Re-throw to propagate the failure
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation (e.g., test ending) - don't log as error
                    throw;
                }
                catch (Exception ex)
                {
                    // Unexpected error - log but continue trying (might be transient)
                    _logger.LogWarning(ex, "Unexpected error sampling system metrics, will retry next interval");
                }
            }

            _logger.LogInformation("SystemMonitor stopping gracefully");
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation (e.g., test ending)
            // Try to collect final sample before stopping
            await CollectFinalSampleAsync();
            _logger.LogInformation("SystemMonitor cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SystemMonitor failed");
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
            _logger.LogInformation("SystemMonitor completed: {Count} samples collected", finalCount);

            _progress?.Report(SystemMonitorPhaseInfo.Completed(
                SystemMonitorPhase.MonitoringStopped,
                sampleCount: finalCount,
                message: $"System monitoring stopped, collected {finalCount} samples"));
        }
    }

    /// <summary>
    /// Samples current system metrics (CPU and memory).
    /// </summary>
    private async Task<SystemMetrics> SampleMetricsAsync(CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var elapsedSeconds = (timestamp - _monitoringStartTime).TotalSeconds;

        double cpuPercent;
        double memoryUsedMb, memoryTotalMb, memoryPercent;

        if (UseWindowsQueries)
        {
            // Query Windows host for CPU and memory (in parallel for better performance)
            var cpuTask = WindowsCpuQuery.QueryAsync(cancellationToken, _logger);
            var memoryTask = WindowsMemoryQuery.QueryAsync(cancellationToken, _logger);
            await Task.WhenAll(cpuTask, memoryTask);

            var windowsCpu = await cpuTask;
            var windowsMemory = await memoryTask;

            // If cancellation was requested, don't treat query failures as errors
            // (they likely failed due to the cancellation, not a real monitoring issue)
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // Fail clearly if Windows queries fail - no fallbacks, no 0 values
            if (!windowsCpu.HasValue)
            {
                throw SystemMonitoringException.WindowsCpuQueryFailed(IsWsl2);
            }
            cpuPercent = windowsCpu.Value;

            if (windowsMemory == null)
            {
                throw SystemMonitoringException.WindowsMemoryQueryFailed(IsWsl2);
            }
            memoryTotalMb = windowsMemory.TotalMb;
            memoryUsedMb = windowsMemory.UsedMb;
            memoryPercent = windowsMemory.Percent;
        }
        else
        {
            // Native Linux: use /proc/stat for CPU and /proc/meminfo for memory
            // Fail clearly if readers are not initialized or fail - no fallbacks
            if (_cpuReader is null)
            {
                throw new InvalidOperationException("CPU reader not initialized for Linux platform. This indicates a bug in SystemMonitorService initialization.");
            }

            if (_memoryReader is null)
            {
                throw new InvalidOperationException("Memory reader not initialized for Linux platform. This indicates a bug in SystemMonitorService initialization.");
            }

            try
            {
                cpuPercent = _cpuReader.Sample();
                if (double.IsNaN(cpuPercent) || cpuPercent < 0)
                {
                    throw SystemMonitoringException.LinuxCpuReadFailed();
                }
            }
            catch (SystemMonitoringException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw SystemMonitoringException.LinuxCpuReadFailed(ex);
            }

            try
            {
                var memory = _memoryReader.Read();
                if (memory.TotalMb <= 0)
                {
                    throw SystemMonitoringException.LinuxMemoryReadFailed();
                }
                memoryTotalMb = memory.TotalMb;
                memoryUsedMb = memory.UsedMb;
                memoryPercent = memory.Percent;
            }
            catch (SystemMonitoringException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw SystemMonitoringException.LinuxMemoryReadFailed(ex);
            }
        }

        return new SystemMetrics(
            Timestamp: timestamp,
            ElapsedSeconds: Math.Round(elapsedSeconds, 3),
            CpuPercent: Math.Round(cpuPercent, 2),
            MemoryUsedMb: memoryUsedMb,
            MemoryTotalMb: memoryTotalMb,
            MemoryPercent: memoryPercent);
    }

    /// <summary>
    /// Attempts to collect final samples before stopping.
    /// Uses a fresh timeout instead of the cancelled token to ensure we get the last measurements.
    /// Collects 2 samples to ensure enough data points for chart alignment with other monitors.
    /// </summary>
    private async Task CollectFinalSampleAsync()
    {
        const int finalSampleCount = 2;

        for (var i = 0; i < finalSampleCount; i++)
        {
            try
            {
                _logger.LogDebug("Collecting final system sample {Current}/{Total} before stopping...", i + 1, finalSampleCount);

                // Use a fresh cancellation token with timeout (not the cancelled one)
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

                var metrics = await SampleMetricsAsync(cts.Token);
                lock (_metricsLock)
                {
                    _collectedMetrics.Add(metrics);
                }

                _logger.LogDebug("Final system sample {Current}/{Total} collected successfully", i + 1, finalSampleCount);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Final sample {Current}/{Total} timed out (15s), stopping", i + 1, finalSampleCount);
                break; // Stop trying if we timeout
            }
            catch (Exception ex)
            {
                // Don't fail the shutdown for final samples - just log and continue
                _logger.LogDebug(ex, "Could not collect final sample {Current}/{Total}, continuing", i + 1, finalSampleCount);
            }
        }
    }
}
