using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// BackgroundService that monitors Docker container resource usage.
/// Samples CPU and memory metrics at regular intervals and stores them in memory.
/// Implements IDockerMonitor to provide access to collected metrics.
/// </summary>
internal sealed class DockerMonitorService : BackgroundService, IDockerMonitor
{
    private readonly string _containerName;
    private readonly TimeSpan _samplingInterval;
    private readonly DockerClientWrapper _dockerClient;
    private readonly ILogger<DockerMonitorService> _logger;
    private readonly ConcurrentBag<DockerMetrics> _collectedMetrics = new();

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
    public IReadOnlyCollection<DockerMetrics> GetCollectedMetrics() =>
        _collectedMetrics
            .OrderBy(m => m.Timestamp)
            .ToList()
            .AsReadOnly();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Docker monitor started for container: {ContainerName}, interval: {Interval}ms",
            _containerName, _samplingInterval.TotalMilliseconds);

        // Use PeriodicTimer for accurate intervals (preferred over Task.Delay loop)
        using var timer = new PeriodicTimer(_samplingInterval);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Wait for next tick
                await timer.WaitForNextTickAsync(stoppingToken);

                // Sample container stats
                var stats = await _dockerClient.GetContainerStatsAsync(
                    _containerName,
                    stoppingToken);

                if (stats == null)
                {
                    // Container not found - log and continue
                    // This allows container to start after monitor
                    _logger.LogDebug("Container {ContainerName} not found, skipping sample",
                        _containerName);
                    continue;
                }

                // Calculate metrics
                var metrics = new DockerMetrics
                {
                    Timestamp = DateTime.UtcNow,
                    ContainerId = stats.ID,
                    ContainerName = _containerName,
                    CpuPercent = DockerClientWrapper.CalculateCpuPercent(stats),
                    MemoryMB = Math.Round(stats.MemoryStats.Usage / 1024.0 / 1024.0, 2)
                };

                // Store metrics directly (thread-safe)
                _collectedMetrics.Add(metrics);

                _logger.LogTrace(
                    "Container {ContainerName} - CPU: {Cpu}%, Memory: {Memory}MB",
                    _containerName, metrics.CpuPercent, metrics.MemoryMB);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Docker monitor stopped for container: {ContainerName}",
                _containerName);
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
        }
    }
}
