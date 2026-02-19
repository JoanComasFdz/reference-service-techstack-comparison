using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring.Connection;
using PerformanceTester.DockerMonitoring.Stats;
using PerformanceTester.DockerMonitoring.ValueObjects;
using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.Monitoring;

/// <summary>
/// Thin BackgroundService shell. Owns MonitorContext and wires the hosted service lifecycle
/// to MonitoringOperations static functions. Contains no business logic.
/// </summary>
internal sealed class DockerMonitorService : BackgroundService
{
    private readonly MonitorContext _ctx;
    private readonly GetContainerIdDelegate _getContainerId;
    private readonly StreamMetricsDelegate _streamMetrics;
    private readonly GetSnapshotDelegate _getSnapshot;
    private readonly InvalidateContainerCacheDelegate _invalidateCache;
    private readonly ILogger<DockerMonitorService> _logger;

    private ReportDockerMonitorProgress _progress = null!;
    private bool _started;

    public string ContainerName => _ctx.ContainerName.Value;

    public DockerMonitorService(
        NonEmptyString containerName,
        GetContainerIdDelegate getContainerId,
        StreamMetricsDelegate streamMetrics,
        GetSnapshotDelegate getSnapshot,
        InvalidateContainerCacheDelegate invalidateCache,
        ILogger<DockerMonitorService> logger)
    {
        _ctx = new MonitorContext(containerName);
        _getContainerId = getContainerId;
        _streamMetrics = streamMetrics;
        _getSnapshot = getSnapshot;
        _invalidateCache = invalidateCache;
        _logger = logger;
    }

    // --- Public API (unchanged surface) ---

    /// <summary>
    /// Pre-warms the Docker API connection to avoid measurement-skewing latency.
    /// The first Docker API call typically takes ~2-3s to establish the HTTP connection
    /// to the Docker socket. This method moves that cost into the Setup phase (before
    /// measurements begin) by resolving the container ID (populating the shared cache)
    /// and fetching a stats snapshot (warming the HTTP connection pool).
    /// Not required for correctness — <see cref="ExecuteAsync"/> resolves the container ID
    /// itself and would work without warmup. This is purely a measurement-accuracy optimization.
    /// </summary>
    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Warming up Docker API for container {ContainerName}...", _ctx.ContainerName);

        var idResult = await _getContainerId(_ctx.ContainerName.Value, cancellationToken);

        if (idResult.IsSuccess)
        {
            await _getSnapshot(idResult.SuccessValue, cancellationToken);
        }

        _logger.LogDebug("Docker API warmup complete for container {ContainerName}", _ctx.ContainerName);
    }

    public async Task StartMonitoringAsync(
        ReportDockerMonitorProgress progress,
        CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            throw new InvalidOperationException(
                $"Monitoring has already been started for container {_ctx.ContainerName}");
        }

        _started = true;
        _progress = progress;

        _progress(DockerMonitorPhaseInfo.Starting(
            DockerMonitorPhase.MonitoringRequested,
            _ctx.ContainerName,
            message: $"Starting streaming monitor for container {_ctx.ContainerName}"));

        _ctx.StartSignal.TrySetResult();

        _logger.LogInformation(
            "StartMonitoring called for container {ContainerName}, waiting for first sample...",
            _ctx.ContainerName);

        await _ctx.FirstSampleCollected.Task.WaitAsync(cancellationToken);

        _logger.LogInformation("First sample collected for container {ContainerName}", _ctx.ContainerName);
    }

    public IReadOnlyCollection<DockerMetrics> GetCollectedMetrics() => _ctx.CollectedMetrics
        .OrderBy(m => m.Timestamp)
        .ToList()
        .AsReadOnly();

    // --- Lifecycle (thin orchestration only) ---

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Docker monitor BackgroundService started for container: {ContainerName}, waiting for StartMonitoring() call...",
            _ctx.ContainerName);

        // Wait for StartMonitoring() to be called (deferred start pattern)
        try
        {
            await _ctx.StartSignal.Task.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Docker monitor stopped before StartMonitoring() was called for container: {ContainerName}",
                _ctx.ContainerName);
            return;
        }

        // Resolve container ID once
        var idResult = await _getContainerId(_ctx.ContainerName.Value, stoppingToken);

        if (idResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to resolve container {ContainerName}: {Error}",
                _ctx.ContainerName,
                idResult.FailureError.Message);
            _ctx.FirstSampleCollected.TrySetResult();
            _progress(DockerMonitorPhaseInfo.Failed(
                DockerMonitorPhase.StreamFailed,
                _ctx.ContainerName,
                message: $"Failed to resolve container: {idResult.FailureError.Message}"));
            return;
        }

        _logger.LogInformation(
            "Docker monitor starting streaming for container: {ContainerName} (ID: {ContainerId})",
            _ctx.ContainerName,
            idResult.SuccessValue[..12]);

        // Start streaming in background task with state machine reconnection
        using var streamingCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        var streamingTask = Task.Run(
            () => MonitoringOperations.RunStreamingLoopAsync(
                _ctx,
                ContainerId.FromString(idResult.SuccessValue),
                _getContainerId,
                _invalidateCache,
                _streamMetrics,
                _progress,
                _logger,
                streamingCts.Token),
            stoppingToken);

        // Wait for first valid stats from stream
        try
        {
            await _ctx.FirstValidStatsReceived.Task
                .WaitAsync(StreamingConstants.FirstStatsTimeout, stoppingToken);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Timeout waiting for first valid stats from container {ContainerName} after {Timeout}s",
                _ctx.ContainerName,
                StreamingConstants.FirstStatsTimeout.TotalSeconds);
        }

        // Wait for cancellation (streaming loop runs independently)
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Docker monitor stopping for container: {ContainerName}",
                _ctx.ContainerName);
        }
        finally
        {
            await streamingCts.CancelAsync();

            try
            {
                await streamingTask.WaitAsync(StreamingConstants.StreamingShutdownTimeout);
            }
            catch
            {
                // Ignore timeout or exceptions during shutdown
            }

            _logger.LogInformation(
                "Docker monitor completed for container: {ContainerName}, collected {Count} samples",
                _ctx.ContainerName,
                _ctx.CollectedMetrics.Count);

            if (!_ctx.StreamingFailed)
            {
                _progress(DockerMonitorPhaseInfo.Completed(
                    DockerMonitorPhase.MonitoringCompleted,
                    _ctx.ContainerName,
                    SampleCount.FromInt(_ctx.CollectedMetrics.Count),
                    message: $"Monitoring completed, collected {_ctx.CollectedMetrics.Count} samples"));
            }
        }
    }
}
