using System.Collections.Concurrent;
using Docker.DotNet.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// BackgroundService that monitors Docker container resource usage using streaming mode.
/// Each Docker stats push is collected directly as a sample (event-driven, no polling).
/// Implements IDockerMonitor to provide access to collected metrics.
/// Supports deferred start pattern - waits for StartMonitoring() before collecting metrics.
/// </summary>
internal sealed class DockerMonitorService : BackgroundService, IDockerMonitor
{
    private readonly string _containerName;
    private readonly DockerClientWrapper _dockerClient;
    private readonly ILogger<DockerMonitorService> _logger;
    private readonly ConcurrentBag<DockerMetrics> _collectedMetrics = new();
    private readonly TaskCompletionSource _startSignal = new();
    private readonly TaskCompletionSource _firstSampleCollected = new();
    private readonly TaskCompletionSource _firstValidStatsReceived = new();

    // Connection success signaling (for streaming loop to detect success)
    private TaskCompletionSource? _pendingConnectionSuccess;

    // Reconnection state
    private volatile int _consecutiveFailures;
    private TimeSpan _currentBackoffDelay = StreamingConstants.InitialReconnectDelay;
    private readonly Lock _backoffLock = new();

    private Task? _streamingTask;
    private CancellationTokenSource? _streamingCts;
    private volatile bool _hasReceivedValidStats;
    private volatile bool _streamingFailed;

    private IProgress<DockerMonitorPhaseInfo>? _progress;
    private bool _started;

    public string ContainerName => _containerName;

    public DockerMonitorService(
        string containerName,
        DockerClientWrapper dockerClient,
        ILogger<DockerMonitorService> logger)
    {
        _containerName = containerName;
        _dockerClient = dockerClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Warming up Docker API for container {ContainerName}...", _containerName);

        // Warm up container ID resolution (will be cached)
        var containerId = await _dockerClient.GetContainerIdAsync(_containerName, cancellationToken);

        if (containerId != null)
        {
            // Make a test snapshot call to warm up the Docker client connection
            await _dockerClient.GetContainerStatsAsync(_containerName, cancellationToken);
        }

        _logger.LogDebug("Docker API warmup complete for container {ContainerName}", _containerName);
    }

    /// <inheritdoc />
    public async Task StartMonitoringAsync(
        IProgress<DockerMonitorPhaseInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            throw new InvalidOperationException($"Monitoring has already been started for container {_containerName}");
        }

        _started = true;
        _progress = progress;

        _progress?.Report(DockerMonitorPhaseInfo.Starting(
            DockerMonitorPhase.MonitoringRequested,
            _containerName,
            message: $"Starting streaming monitor for container {_containerName}"));

        _startSignal.TrySetResult();
        _logger.LogInformation(
            "StartMonitoring called for container {ContainerName}, waiting for first sample...", _containerName);

        // Wait for the first sample to be collected
        await _firstSampleCollected.Task.WaitAsync(cancellationToken);

        _logger.LogInformation("First sample collected for container {ContainerName}", _containerName);
    }

    /// <inheritdoc />
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
            _logger.LogInformation("Docker monitor stopped before StartMonitoring() was called for container: {ContainerName}",
                _containerName);
            return;
        }

        // Resolve container ID once
        string? containerId;
        try
        {
            containerId = await _dockerClient.GetContainerIdAsync(_containerName, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve container {ContainerName}", _containerName);
            _firstSampleCollected.TrySetResult(); // Unblock caller
            _progress?.Report(DockerMonitorPhaseInfo.Failed(
                DockerMonitorPhase.StreamFailed,
                _containerName,
                message: $"Failed to resolve container: {ex.Message}"));
            return;
        }

        if (containerId == null)
        {
            _logger.LogWarning("Container {ContainerName} not found, cannot start monitoring", _containerName);
            _firstSampleCollected.TrySetResult(); // Unblock caller
            _progress?.Report(DockerMonitorPhaseInfo.Failed(
                DockerMonitorPhase.StreamFailed,
                _containerName,
                message: $"Container '{_containerName}' not found"));
            return;
        }

        _logger.LogInformation(
            "Docker monitor starting streaming for container: {ContainerName} (ID: {ContainerId})",
            _containerName, containerId[..12]);

        // Start streaming in background task with reconnection support
        _streamingCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _streamingTask = Task.Run(
            () => RunStreamingLoopWithReconnectionAsync(containerId, _streamingCts.Token),
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
                _containerName, StreamingConstants.FirstStatsTimeout.TotalSeconds);
        }

        // Wait for cancellation (streaming loop runs independently, samples collected in OnStatsReceived)
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
            // Stop streaming and dispose CTS
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
                _containerName, _collectedMetrics.Count);

            // Report completion (success case only - failures reported elsewhere)
            if (!_streamingFailed)
            {
                _progress?.Report(DockerMonitorPhaseInfo.Completed(
                    DockerMonitorPhase.MonitoringCompleted,
                    _containerName,
                    sampleCount: _collectedMetrics.Count,
                    message: $"Monitoring completed, collected {_collectedMetrics.Count} samples"));
            }
        }
    }

    /// <summary>
    /// Background task that manages the streaming connection with automatic reconnection.
    /// This method owns all connection lifecycle phase reporting.
    /// </summary>
    private async Task RunStreamingLoopWithReconnectionAsync(
        string initialContainerId,
        CancellationToken cancellationToken)
    {
        var currentContainerId = initialContainerId;

        while (!cancellationToken.IsCancellationRequested)
        {
            // === REPORT: Connecting ===
            _progress?.Report(DockerMonitorPhaseInfo.Starting(
                DockerMonitorPhase.StreamConnecting,
                _containerName,
                message: _consecutiveFailures > 0
                    ? $"Reconnecting to {_containerName} (attempt {_consecutiveFailures + 1})..."
                    : $"Connecting to {_containerName}..."));

            // Create fresh TCS for this connection attempt
            var connectionSuccessTcs = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingConnectionSuccess = connectionSuccessTcs;
            _hasReceivedValidStats = false;

            try
            {
                // Start streaming in nested task so we can race against success signal
                using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                var streamTask = Task.Run(async () =>
                {
                    await _dockerClient.StartStatsStreamAsync(
                        currentContainerId,
                        OnStatsReceived,
                        streamCts.Token);
                }, cancellationToken);

                // Race: connection success vs stream failure
                var completedTask = await Task.WhenAny(
                    connectionSuccessTcs.Task,
                    streamTask);

                if (completedTask == connectionSuccessTcs.Task
                    && connectionSuccessTcs.Task.IsCompletedSuccessfully)
                {
                    HandleConnectionSuccess();
                    await streamTask;
                    // Stream ended without exception (unexpected for Stream=true)
                    _logger.LogWarning(
                        "Stats stream ended unexpectedly for container {ContainerName}",
                        _containerName);
                    break;
                }
                else
                {
                    // Stream task completed before success signal - must be error
                    await streamTask; // Re-throws the exception
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Streaming cancelled for container {ContainerName}", _containerName);
                break;
            }
            catch (Exception ex)
            {
                if (!ShouldRetry(ex))
                {
                    break;
                }

                currentContainerId = await AttemptReconnectionAsync(ex, currentContainerId, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Reports successful connection and resets failure tracking state.
    /// Called when the connection success TCS completes, indicating valid stats were received.
    /// </summary>
    private void HandleConnectionSuccess()
    {
        // === REPORT: Connected ===
        var wasReconnecting = _consecutiveFailures > 0;
        _progress?.Report(DockerMonitorPhaseInfo.Completed(
            DockerMonitorPhase.StreamConnected,
            _containerName,
            message: wasReconnecting
                ? $"Reconnected to {_containerName}"
                : $"Connected to {_containerName}"));

        // Reset failure tracking on successful connection
        _consecutiveFailures = 0;
        lock (_backoffLock)
        {
            _currentBackoffDelay = StreamingConstants.InitialReconnectDelay;
        }
    }

    /// <summary>
    /// Determines whether a reconnection should be attempted after a streaming failure.
    /// Increments the failure counter, invalidates the container cache, and reports permanent
    /// failure if the maximum retry count has been exceeded.
    /// </summary>
    /// <returns>true if a reconnection attempt should be made; false if the loop should exit.</returns>
    private bool ShouldRetry(Exception ex)
    {
        _consecutiveFailures++;
        _dockerClient.InvalidateContainerCache(_containerName);

        if (_consecutiveFailures <= StreamingConstants.MaxReconnectAttempts)
        {
            return true;
        }

        // === REPORT: Failed Permanently ===
        _logger.LogError(ex,
            "Streaming failed permanently after {Failures} attempts for container {ContainerName}",
            _consecutiveFailures, _containerName);

        _progress?.Report(DockerMonitorPhaseInfo.Failed(
            DockerMonitorPhase.StreamFailed,
            _containerName,
            message: $"Connection failed permanently after {_consecutiveFailures} attempts"));

        _streamingFailed = true;
        return false;
    }

    /// <summary>
    /// Calculates the next reconnection delay using exponential backoff with jitter.
    /// Thread-safe: uses <see cref="_backoffLock"/> to protect shared backoff state.
    /// </summary>
    /// <returns>The delay to wait before the next reconnection attempt.</returns>
    private TimeSpan CalculateReconnectionDelay()
    {
        lock (_backoffLock)
        {
            var delayToUse = _currentBackoffDelay;
            var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
            _currentBackoffDelay = TimeSpan.FromTicks(Math.Min(
                _currentBackoffDelay.Ticks * 2,
                StreamingConstants.MaxReconnectDelay.Ticks)) + jitter;
            return delayToUse;
        }
    }

    /// <summary>
    /// Performs a reconnection attempt: waits for the backoff delay, then resolves the current
    /// container ID (which may have changed if the container was restarted).
    /// </summary>
    /// <returns>The resolved container ID to use for the next connection attempt.</returns>
    private async Task<string> AttemptReconnectionAsync(
        Exception ex,
        string currentContainerId,
        CancellationToken cancellationToken)
    {
        var delayToUse = CalculateReconnectionDelay();

        // === REPORT: Disconnected (will retry) ===
        _logger.LogWarning(ex,
            "Stream disconnected for container {ContainerName}, " +
            "reconnecting in {Delay:F1}s (attempt {Count}/{Max})",
            _containerName, delayToUse.TotalSeconds,
            _consecutiveFailures, StreamingConstants.MaxReconnectAttempts);

        _progress?.Report(DockerMonitorPhaseInfo.Failed(
            DockerMonitorPhase.StreamDisconnected,
            _containerName,
            message: $"Disconnected, retrying in {delayToUse.TotalSeconds:F1}s " +
                     $"(attempt {_consecutiveFailures}/{StreamingConstants.MaxReconnectAttempts})"));

        await Task.Delay(delayToUse, cancellationToken);

        // Try to resolve new container ID (may have restarted)
        var newContainerId = await _dockerClient.GetContainerIdAsync(
            _containerName, cancellationToken);

        if (newContainerId == null)
        {
            _logger.LogWarning(
                "Container {ContainerName} not found during reconnection",
                _containerName);
            // Return current ID - loop will increment failure count on next iteration
            return currentContainerId;
        }

        return newContainerId;
    }

    /// <summary>
    /// Handles stats pushed by Docker. Collects a sample directly from each push (event-driven).
    /// Signals connection success via TCS.
    /// Does NOT report phases - that's the streaming loop's responsibility.
    /// </summary>
    private void OnStatsReceived(ContainerStatsResponse stats)
    {
        // Validate first stats (PreCPUStats must be valid for CPU calculation)
        if (!_hasReceivedValidStats)
        {
            if (!DockerClientWrapper.HasValidPreCpuStats(stats))
            {
                _logger.LogDebug(
                    "Skipping stats with invalid PreCPUStats for container {ContainerName}",
                    _containerName);
                return;
            }

            _hasReceivedValidStats = true;

            // Signal to ExecuteAsync that we can start sampling
            _firstValidStatsReceived.TrySetResult();

            // Signal to streaming loop that connection succeeded
            // (streaming loop will report the StreamConnected phase)
            _pendingConnectionSuccess?.TrySetResult();
        }

        // Calculate and collect the sample directly (no intermediate storage)
        var metrics = new DockerMetrics
        {
            Timestamp = DateTime.UtcNow,
            ContainerId = stats.ID,
            ContainerName = _containerName,
            CpuPercent = DockerClientWrapper.CalculateCpuPercent(stats),
            MemoryMB = Math.Round(stats.MemoryStats.Usage / 1024.0 / 1024.0, 2)
        };

        _collectedMetrics.Add(metrics);
        _firstSampleCollected.TrySetResult();

        _logger.LogDebug(
            "Sample #{Count} for container {ContainerName} - CPU: {Cpu}%, Memory: {Memory}MB",
            _collectedMetrics.Count, _containerName, metrics.CpuPercent, metrics.MemoryMB);
    }
}
