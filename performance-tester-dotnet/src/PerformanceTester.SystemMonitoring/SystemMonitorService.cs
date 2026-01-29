using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// BackgroundService that monitors system-wide CPU and memory usage.
/// Implements deferred start pattern: waits for StartMonitoringAsync() call.
/// Uses /proc/stat for CPU and /proc/meminfo for memory.
/// Has special WSL2 handling for accurate Windows host memory.
/// </summary>
internal sealed class SystemMonitorService : BackgroundService, ISystemMonitor
{
    private readonly TimeSpan _samplingInterval;
    private readonly ILogger<SystemMonitorService> _logger;
    private readonly ConcurrentBag<SystemMetrics> _collectedMetrics = new();
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

    public SystemMonitorService(
        TimeSpan samplingInterval,
        ILogger<SystemMonitorService> logger)
    {
        if (samplingInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(samplingInterval), samplingInterval, "Sampling interval must be positive");

        _samplingInterval = samplingInterval;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Detect environment at construction
        IsWsl2 = Wsl2Detector.IsWsl2();
        CpuCount = Environment.ProcessorCount;

        if (IsWsl2)
        {
            _logger.LogInformation("Detected WSL2 environment - will query Windows host for memory");
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
    public IReadOnlyCollection<SystemMetrics> GetCollectedMetrics() => _collectedMetrics
        .OrderBy(m => m.Timestamp)
        .ToList()
        .AsReadOnly();

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

            _logger.LogInformation("SystemMonitor starting, sampling every {IntervalMs}ms, CPU cores: {CpuCount}, WSL2: {IsWsl2}",
                _samplingInterval.TotalMilliseconds,
                CpuCount,
                IsWsl2);

            // Initialize readers
            _cpuReader = new ProcStatReader();
            _memoryReader = new ProcMeminfoReader();

            // Initialize CPU reader (first sample returns 0.0)
            _cpuReader.Sample();

            _monitoringStartTime = DateTimeOffset.UtcNow;

            // Create timer for periodic sampling
            using var timer = new PeriodicTimer(_samplingInterval);

            // Sample until cancellation
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var metrics = await SampleMetricsAsync(stoppingToken);
                    _collectedMetrics.Add(metrics);

                    var currentCount = _collectedMetrics.Count;

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
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sampling system metrics");
                }
            }

            _logger.LogInformation("SystemMonitor stopping gracefully");
        }
        catch (OperationCanceledException)
        {
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
            _logger.LogInformation("SystemMonitor completed: {Count} samples collected", _collectedMetrics.Count);

            _progress?.Report(SystemMonitorPhaseInfo.Completed(
                SystemMonitorPhase.MonitoringStopped,
                sampleCount: _collectedMetrics.Count,
                message: $"System monitoring stopped, collected {_collectedMetrics.Count} samples"));
        }
    }

    /// <summary>
    /// Samples current system metrics (CPU and memory).
    /// </summary>
    private async Task<SystemMetrics> SampleMetricsAsync(CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var elapsedSeconds = (timestamp - _monitoringStartTime).TotalSeconds;

        // Sample CPU from /proc/stat
        var cpuPercent = _cpuReader!.Sample();

        // Sample memory
        double memoryUsedMb, memoryTotalMb, memoryPercent;

        if (IsWsl2)
        {
            // Query Windows host for accurate memory
            var windowsMemory = await WindowsMemoryQuery.QueryAsync(cancellationToken);
            if (windowsMemory != null)
            {
                memoryTotalMb = windowsMemory.TotalMb;
                memoryUsedMb = windowsMemory.UsedMb;
                memoryPercent = windowsMemory.Percent;
            }
            else
            {
                // Fallback to /proc/meminfo if PowerShell fails
                var linuxMemory = _memoryReader!.Read();
                memoryTotalMb = linuxMemory.TotalMb;
                memoryUsedMb = linuxMemory.UsedMb;
                memoryPercent = linuxMemory.Percent;
            }
        }
        else
        {
            // Native Linux: use /proc/meminfo
            var memory = _memoryReader!.Read();
            memoryTotalMb = memory.TotalMb;
            memoryUsedMb = memory.UsedMb;
            memoryPercent = memory.Percent;
        }

        return new SystemMetrics(
            Timestamp: timestamp,
            ElapsedSeconds: Math.Round(elapsedSeconds, 3),
            CpuPercent: Math.Round(cpuPercent, 2),
            MemoryUsedMb: memoryUsedMb,
            MemoryTotalMb: memoryTotalMb,
            MemoryPercent: memoryPercent);
    }
}
