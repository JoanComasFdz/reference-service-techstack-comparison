using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.DockerMonitoring.Connection;
using PerformanceTester.DockerMonitoring.Stats;
using PerformanceTester.DockerMonitoring.ValueObjects;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.Monitoring;

/// <summary>
/// BackgroundService that monitors Docker container resource usage using streaming mode.
/// Uses explicit ConnectionState machine for reconnection logic and IAsyncEnumerable for streaming.
/// Named delegates in DI provide public access to collected metrics.
/// Supports deferred start pattern — waits for StartMonitoring() before collecting metrics.
/// </summary>
internal sealed class DockerMonitorService : BackgroundService
{
    private readonly NonEmptyString _containerName;
    private readonly DockerClientWrapper _dockerClient;
    private readonly ILogger<DockerMonitorService> _logger;
    private readonly ConcurrentBag<DockerMetrics> _collectedMetrics = new();
    private readonly TaskCompletionSource _startSignal = new();
    private readonly TaskCompletionSource _firstSampleCollected = new();
    private readonly TaskCompletionSource _firstValidStatsReceived = new();

    private Task? _streamingTask;
    private CancellationTokenSource? _streamingCts;
    private volatile bool _streamingFailed;

    private ReportDockerMonitorProgress _progress = null!;
    private bool _started;

    public string ContainerName => _containerName.Value;

    public DockerMonitorService(
        NonEmptyString containerName,
        DockerClientWrapper dockerClient,
        ILogger<DockerMonitorService> logger)
    {
        _containerName = containerName;
        _dockerClient = dockerClient;
        _logger = logger;
    }

    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Warming up Docker API for container {ContainerName}...", _containerName);

        var containerId = await _dockerClient.GetContainerIdAsync(_containerName.Value, cancellationToken);

        if (containerId != null)
        {
            await _dockerClient.GetContainerStatsAsync(_containerName.Value, cancellationToken);
        }

        _logger.LogDebug("Docker API warmup complete for container {ContainerName}", _containerName);
    }

    public async Task StartMonitoringAsync(
        ReportDockerMonitorProgress progress,
        CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            throw new InvalidOperationException($"Monitoring has already been started for container {_containerName}");
        }

        _started = true;
        _progress = progress;

        _progress(DockerMonitorPhaseInfo.Starting(
            DockerMonitorPhase.MonitoringRequested,
            _containerName.Value,
            message: $"Starting streaming monitor for container {_containerName}"));

        _startSignal.TrySetResult();
        _logger.LogInformation(
            "StartMonitoring called for container {ContainerName}, waiting for first sample...",
            _containerName);

        await _firstSampleCollected.Task.WaitAsync(cancellationToken);

        _logger.LogInformation("First sample collected for container {ContainerName}", _containerName);
    }

    public IReadOnlyCollection<DockerMetrics> GetCollectedMetrics() => _collectedMetrics
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
            _logger.LogInformation(
                "Docker monitor stopped before StartMonitoring() was called for container: {ContainerName}",
                _containerName);
            return;
        }

        // Resolve container ID once
        string? containerId;
        try
        {
            containerId = await _dockerClient.GetContainerIdAsync(_containerName.Value, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve container {ContainerName}", _containerName);
            _firstSampleCollected.TrySetResult();
            _progress(DockerMonitorPhaseInfo.Failed(
                DockerMonitorPhase.StreamFailed,
                _containerName.Value,
                message: $"Failed to resolve container: {ex.Message}"));
            return;
        }

        if (containerId == null)
        {
            _logger.LogWarning("Container {ContainerName} not found, cannot start monitoring", _containerName);
            _firstSampleCollected.TrySetResult();
            _progress(DockerMonitorPhaseInfo.Failed(
                DockerMonitorPhase.StreamFailed,
                _containerName.Value,
                message: $"Container '{_containerName}' not found"));
            return;
        }

        _logger.LogInformation(
            "Docker monitor starting streaming for container: {ContainerName} (ID: {ContainerId})",
            _containerName,
            containerId[..12]);

        // Start streaming in background task with state machine reconnection
        _streamingCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _streamingTask = Task.Run(
            () => RunStreamingLoopAsync(ContainerId.FromString(containerId), _streamingCts.Token),
            stoppingToken);

        // Wait for first valid stats from stream
        try
        {
            await _firstValidStatsReceived.Task.WaitAsync(StreamingConstants.FirstStatsTimeout, stoppingToken);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Timeout waiting for first valid stats from container {ContainerName} after {Timeout}s",
                _containerName,
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
                _containerName);
        }
        finally
        {
            _streamingCts?.Cancel();

            try
            {
                if (_streamingTask != null)
                {
                    await _streamingTask.WaitAsync(StreamingConstants.StreamingShutdownTimeout);
                }
            }
            catch { /* Ignore timeout or exceptions */ }

            _streamingCts?.Dispose();

            _logger.LogInformation(
                "Docker monitor completed for container: {ContainerName}, collected {Count} samples",
                _containerName,
                _collectedMetrics.Count);

            if (!_streamingFailed)
            {
                _progress(DockerMonitorPhaseInfo.Completed(
                    DockerMonitorPhase.MonitoringCompleted,
                    _containerName.Value,
                    sampleCount: _collectedMetrics.Count,
                    message: $"Monitoring completed, collected {_collectedMetrics.Count} samples"));
            }
        }
    }

    /// <summary>
    /// State machine interpreter loop. Pure <see cref="ConnectionStateMachine.Transition"/>
    /// decides state changes; this method interprets states as effects.
    /// </summary>
    private async Task RunStreamingLoopAsync(
        ContainerId initialContainerId,
        CancellationToken cancellationToken)
    {
        ConnectionState state = new ConnectionState.Connecting(
            initialContainerId,
            AttemptCount.FromInt(0),
            StreamingConstants.InitialReconnectDelay);

        while (state is not ConnectionState.Failed && !cancellationToken.IsCancellationRequested)
        {
            _progress(PhaseReporting.ToPhaseInfo(state, _containerName));

            state = state switch
            {
                ConnectionState.Connecting c => await ExecuteStreamingSessionAsync(c, cancellationToken),
                ConnectionState.Disconnected d => await ExecuteReconnectAsync(d, cancellationToken),
                _ => state
            };
        }

        if (state is ConnectionState.Failed)
        {
            _progress(PhaseReporting.ToPhaseInfo(state, _containerName));
            _streamingFailed = true;

            _logger.LogError(
                "Streaming failed permanently for container {ContainerName}",
                _containerName);
        }
    }

    /// <summary>
    /// Starts a streaming session: iterates the IAsyncEnumerable metrics stream,
    /// signals connection success on first valid item, collects all metrics.
    /// Returns the next state based on how the stream ended.
    /// </summary>
    private async Task<ConnectionState> ExecuteStreamingSessionAsync(
        ConnectionState.Connecting connectingState,
        CancellationToken cancellationToken)
    {
        ConnectionState currentState = connectingState;

        try
        {
            await foreach (var metrics in _dockerClient.StreamMetrics(
                connectingState.ContainerId.Value, _containerName, cancellationToken))
            {
                if (currentState is ConnectionState.Connecting)
                {
                    // First valid stats → transition to Connected
                    currentState = ConnectionStateMachine.Transition(
                        currentState,
                        new StreamEvent.StatsReceived(),
                        StreamingConstants.MaxReconnectAttempts,
                        StreamingConstants.MaxReconnectDelay);

                    _progress(PhaseReporting.ToPhaseInfo(currentState, _containerName));
                    _firstValidStatsReceived.TrySetResult();
                }

                _collectedMetrics.Add(metrics);
                _firstSampleCollected.TrySetResult();

                _logger.LogDebug(
                    "Sample #{Count} for container {ContainerName} - CPU: {Cpu}%, Memory: {Memory}MB",
                    _collectedMetrics.Count,
                    _containerName,
                    metrics.CpuPercent,
                    metrics.MemoryMB);
            }

            // Stream ended without error (unexpected for Stream=true)
            _logger.LogWarning(
                "Stats stream ended unexpectedly for container {ContainerName}",
                _containerName);

            return currentState;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Streaming cancelled for container {ContainerName}", _containerName);
            return currentState;
        }
        catch (Exception ex)
        {
            _dockerClient.InvalidateContainerCache(_containerName.Value);

            var nextState = ConnectionStateMachine.Transition(
                currentState,
                new StreamEvent.Error(ex),
                StreamingConstants.MaxReconnectAttempts,
                StreamingConstants.MaxReconnectDelay);

            if (nextState is ConnectionState.Disconnected d)
            {
                _logger.LogWarning(
                    ex,
                    "Stream disconnected for container {ContainerName}, retrying in {Delay:F1}s (attempt {Count}/{Max})",
                    _containerName,
                    d.NextBackoff.TotalSeconds,
                    d.ConsecutiveFailures,
                    StreamingConstants.MaxReconnectAttempts);
            }

            return nextState;
        }
    }

    /// <summary>
    /// Waits for the backoff delay, re-resolves the container ID (handles restarts),
    /// and transitions to a new Connecting state.
    /// </summary>
    private async Task<ConnectionState> ExecuteReconnectAsync(
        ConnectionState.Disconnected disconnectedState,
        CancellationToken cancellationToken)
    {
        try
        {
            var backoff = ReconnectionPolicy.CalculateBackoff(
                disconnectedState.NextBackoff, StreamingConstants.MaxReconnectDelay);

            await Task.Delay(backoff.DelayToUse, cancellationToken);

            _dockerClient.InvalidateContainerCache(_containerName.Value);

            var newContainerId = await _dockerClient.GetContainerIdAsync(
                _containerName.Value, cancellationToken);

            if (newContainerId == null)
            {
                _logger.LogWarning(
                    "Container {ContainerName} not found during reconnection",
                    _containerName);
            }

            var resolvedContainerId = newContainerId != null
                ? ContainerId.FromString(newContainerId)
                : disconnectedState.ContainerId;

            return new ConnectionState.Connecting(
                resolvedContainerId,
                disconnectedState.ConsecutiveFailures,
                backoff.NextBackoff);
        }
        catch (OperationCanceledException)
        {
            return disconnectedState;
        }
    }
}
