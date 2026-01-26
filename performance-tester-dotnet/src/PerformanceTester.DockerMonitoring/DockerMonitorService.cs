using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// BackgroundService that monitors Docker container resource usage.
/// Samples CPU and memory metrics at regular intervals and stores them in memory.
/// Implements IDockerMonitor to provide access to collected metrics.
/// Supports deferred start pattern - waits for StartMonitoring() before collecting metrics.
/// </summary>
internal sealed class DockerMonitorService : BackgroundService, IDockerMonitor
{
    private readonly string _containerName;
    private readonly TimeSpan _samplingInterval;
    private readonly DockerClientWrapper _dockerClient;
    private readonly ILogger<DockerMonitorService> _logger;
    private readonly ConcurrentBag<DockerMetrics> _collectedMetrics = new();
    private readonly TaskCompletionSource _startSignal = new();
    private readonly TaskCompletionSource _firstSampleCollected = new();

    private IProgress<DockerMonitorPhaseInfo>? _progress;
    private bool _started;

    public string ContainerName => _containerName;

    public DockerMonitorService(
        string containerName,
        TimeSpan samplingInterval,
        DockerClientWrapper dockerClient,
        ILogger<DockerMonitorService> logger)
    {
        _containerName = containerName;
        _samplingInterval = samplingInterval;
        _dockerClient = dockerClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Warming up Docker API for container {ContainerName}...", _containerName);

        // Make a test call to warm up the Docker client connection
        // This ensures the first real call during the test is fast
        await _dockerClient.GetContainerStatsAsync(_containerName, cancellationToken);

        _logger.LogDebug("Docker API warmup complete for container {ContainerName}", _containerName);
    }

    /// <inheritdoc />
    public async Task StartMonitoringAsync(
        IProgress<DockerMonitorPhaseInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_started)
            throw new InvalidOperationException($"Monitoring has already been started for container {_containerName}");

        _started = true;
        _progress = progress;  // Store for use in ExecuteAsync

        // Report phase: MonitoringRequested/Starting
        _progress?.Report(DockerMonitorPhaseInfo.Starting(
            DockerMonitorPhase.MonitoringRequested,
            _containerName,
            message: $"Starting monitoring for container {_containerName}"));

        _startSignal.TrySetResult();
        _logger.LogInformation("StartMonitoring called for container {ContainerName}, waiting for first sample...", _containerName);

        // Wait for the first sample to be collected
        await _firstSampleCollected.Task.WaitAsync(cancellationToken);

        _logger.LogInformation("First sample collected for container {ContainerName}", _containerName);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<DockerMetrics> GetCollectedMetrics() =>
        _collectedMetrics
            .OrderBy(m => m.Timestamp)
            .ToList()
            .AsReadOnly();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Docker monitor BackgroundService started for container: {ContainerName}, waiting for StartMonitoring() call...",
            _containerName);

        // Wait for StartMonitoring() to be called (deferred start pattern)
        try
        {
            await _startSignal.Task.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Docker monitor stopped before StartMonitoring() was called for container: {ContainerName}",
                _containerName);
            return;
        }

        _logger.LogInformation(
            "Docker monitor starting for container: {ContainerName}, interval: {Interval}ms",
            _containerName, _samplingInterval.TotalMilliseconds);

        // Use PeriodicTimer for accurate intervals (preferred over Task.Delay loop)
        using var timer = new PeriodicTimer(_samplingInterval);

        try
        {
            // Sample-first pattern: collect initial sample immediately before entering timer loop
            // This ensures we capture data from the very start of monitoring
            await SampleContainerAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Wait for next tick
                await timer.WaitForNextTickAsync(stoppingToken);

                // Sample container stats
                await SampleContainerAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Docker monitor stopping for container: {ContainerName}, attempting final sample...",
                _containerName);

            // Try to collect one final sample with a fresh token and timeout
            // This ensures we capture data even when the cancellation token was triggered
            // while a Docker API call was in flight (which takes 2-3 seconds)
            await TryCollectFinalSampleAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Docker monitor failed for container: {ContainerName}",
                _containerName);
            throw;
        }
        finally
        {
            _logger.LogInformation(
                "Docker monitor completed for container: {ContainerName}, collected {Count} samples",
                _containerName, _collectedMetrics.Count);

            // Report phase: MonitoringStopped/Completed
            _progress?.Report(DockerMonitorPhaseInfo.Completed(
                DockerMonitorPhase.MonitoringStopped,
                _containerName,
                sampleCount: _collectedMetrics.Count,
                message: $"Monitoring stopped for {_containerName}, collected {_collectedMetrics.Count} samples"));
        }
    }

    /// <summary>
    /// Samples container stats and stores the metrics.
    /// </summary>
    private async Task SampleContainerAsync(CancellationToken stoppingToken)
    {
        // Record timestamp BEFORE the Docker API call
        // This ensures the timestamp reflects when we started sampling, not when we finished
        // (Docker API calls can take 1-3 seconds)
        var sampleTimestamp = DateTime.UtcNow;

        var stats = await _dockerClient.GetContainerStatsAsync(
            _containerName,
            stoppingToken);

        if (stats == null)
        {
            // Container not found - log and continue
            // This allows container to start after monitor
            // Still signal first sample to avoid blocking StartMonitoringAsync
            _logger.LogWarning("Container {ContainerName} not found, skipping sample",
                _containerName);
            _firstSampleCollected.TrySetResult();

            // Report phase: ContainerNotFound/Completed
            _progress?.Report(DockerMonitorPhaseInfo.Completed(
                DockerMonitorPhase.ContainerNotFound,
                _containerName,
                message: $"Container {_containerName} not found"));
            return;
        }

        // Calculate metrics
        var metrics = new DockerMetrics
        {
            Timestamp = sampleTimestamp,
            ContainerId = stats.ID,
            ContainerName = _containerName,
            CpuPercent = DockerClientWrapper.CalculateCpuPercent(stats),
            MemoryMB = Math.Round(stats.MemoryStats.Usage / 1024.0 / 1024.0, 2)
        };

        // Store metrics directly (thread-safe)
        _collectedMetrics.Add(metrics);

        var currentCount = _collectedMetrics.Count;

        // Signal first sample and report phases
        var isFirstSample = _firstSampleCollected.TrySetResult();
        if (isFirstSample)
        {
            _progress?.Report(DockerMonitorPhaseInfo.Completed(
                DockerMonitorPhase.FirstSampleCollected,
                _containerName,
                sampleCount: currentCount,
                message: $"First sample collected for {_containerName}"));
        }

        // Always report SampleCollected with current count
        _progress?.Report(DockerMonitorPhaseInfo.Completed(
            DockerMonitorPhase.SampleCollected,
            _containerName,
            sampleCount: currentCount,
            message: $"Sample #{currentCount} collected"));

        _logger.LogDebug(
            "Sample #{SampleCount} for container {ContainerName} at {Timestamp:HH:mm:ss.fff} - CPU: {Cpu}%, Memory: {Memory}MB",
            currentCount, _containerName, metrics.Timestamp, metrics.CpuPercent, metrics.MemoryMB);
    }

    /// <summary>
    /// Attempts to collect a final sample when the monitor is being stopped.
    /// Uses a fresh cancellation token with a 5-second timeout to avoid getting stuck.
    /// This ensures we capture data even when the original cancellation token was triggered
    /// while a Docker API call was in flight.
    /// </summary>
    private async Task TryCollectFinalSampleAsync()
    {
        try
        {
            using var finalSampleCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var sampleTimestamp = DateTime.UtcNow;
            var stats = await _dockerClient.GetContainerStatsAsync(_containerName, finalSampleCts.Token);

            if (stats == null)
            {
                _logger.LogDebug(
                    "Container {ContainerName} not found for final sample",
                    _containerName);
                return;
            }

            var metrics = new DockerMetrics
            {
                Timestamp = sampleTimestamp,
                ContainerId = stats.ID,
                ContainerName = _containerName,
                CpuPercent = DockerClientWrapper.CalculateCpuPercent(stats),
                MemoryMB = Math.Round(stats.MemoryStats.Usage / 1024.0 / 1024.0, 2)
            };

            _collectedMetrics.Add(metrics);

            _logger.LogInformation(
                "Final sample #{SampleCount} collected for container {ContainerName} at {Timestamp:HH:mm:ss.fff} - CPU: {Cpu}%, Memory: {Memory}MB",
                _collectedMetrics.Count, _containerName, metrics.Timestamp, metrics.CpuPercent, metrics.MemoryMB);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Final sample collection timed out for container {ContainerName}",
                _containerName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to collect final sample for container {ContainerName}",
                _containerName);
        }
    }
}
