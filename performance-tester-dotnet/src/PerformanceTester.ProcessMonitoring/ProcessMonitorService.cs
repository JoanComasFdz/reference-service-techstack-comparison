using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace PerformanceTester.ProcessMonitoring;

/// <summary>
/// BackgroundService that monitors process resource usage and stores metrics in memory.
/// Samples CPU, memory, and thread count at regular intervals using PeriodicTimer.
/// Implements IProcessMonitor to provide access to collected metrics.
/// Supports deferred start pattern - process ID is provided via StartMonitoring() after service starts.
/// </summary>
internal sealed class ProcessMonitorService : BackgroundService, IProcessMonitor
{
    private readonly TimeSpan _samplingInterval;
    private readonly ILogger<ProcessMonitorService> _logger;
    private readonly ConcurrentBag<ProcessMetrics> _collectedMetrics = new();
    private readonly TaskCompletionSource<int> _processIdSource = new();

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
    public void StartMonitoring(int processId)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId), processId, "Process ID must be positive");

        if (_processId.HasValue)
            throw new InvalidOperationException($"Monitoring has already been started for process {_processId.Value}");

        _processId = processId;
        _processIdSource.TrySetResult(processId);
        _logger.LogInformation("StartMonitoring called for PID {ProcessId}", processId);
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
            _logger.LogInformation("ProcessMonitor BackgroundService started, waiting for StartMonitoring() call...");

            // Wait for StartMonitoring() to be called with process ID
            int processId;
            try
            {
                processId = await _processIdSource.Task.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("ProcessMonitor stopped before StartMonitoring() was called");
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
                }
                catch (InvalidOperationException)
                {
                    // Process no longer exists (HasExited threw)
                    _logger.LogWarning("⚠️ Process {ProcessId} terminated during sampling", processId);
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
            throw;
        }
        finally
        {
            _logger.LogInformation("✓ ProcessMonitor completed: {Count} samples collected", _collectedMetrics.Count);

            // Dispose process handle
            process?.Dispose();
        }
    }
}
