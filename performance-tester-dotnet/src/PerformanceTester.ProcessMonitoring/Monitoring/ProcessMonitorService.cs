using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.ProcessMonitoring.ValueObjects;

namespace PerformanceTester.ProcessMonitoring.Monitoring;

/// <summary>
/// Thin shell (Guideline 05-06): owns <see cref="MonitorContext"/>, wires lifecycle,
/// delegates all logic to <see cref="MonitoringOperations"/>.
/// </summary>
internal sealed class ProcessMonitorService : BackgroundService
{
    private readonly MonitorContext _ctx = new();
    private readonly TimeSpan _samplingInterval;
    private readonly ILogger<ProcessMonitorService> _logger;

    private bool _started;
    private ProcessId? _processId;
    private ReportProcessMonitorProgressDelegate? _reportProgress;

    public ProcessMonitorService(
        TimeSpan samplingInterval,
        ILogger<ProcessMonitorService> logger)
    {
        if (samplingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(samplingInterval),
                samplingInterval,
                "Sampling interval must be positive");
        }

        _samplingInterval = samplingInterval;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // -- Public API (exposed via delegates in DI) ------------------------------------

    /// <summary>
    /// Starts monitoring the specified process.
    /// Blocks until the first sample has been collected.
    /// </summary>
    public async Task StartMonitoringAsync(
        ProcessId processId,
        ReportProcessMonitorProgressDelegate reportProgress,
        CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            throw new InvalidOperationException(
                $"Monitoring has already been started for process {_processId}");
        }

        _started = true;
        _processId = processId;
        _reportProgress = reportProgress;

        reportProgress(ProcessMonitorPhaseInfo.Starting(
            ProcessMonitorPhase.MonitoringRequested,
            processId,
            message: $"Starting monitoring for process {processId}"));

        _ctx.StartSignal.TrySetResult();
        _logger.LogInformation(
            "StartMonitoringAsync called for PID {ProcessId}, waiting for first sample...",
            processId);

        await _ctx.FirstSampleCollected.Task.WaitAsync(cancellationToken);
        _logger.LogInformation("First sample collected for PID {ProcessId}", processId);
    }

    /// <summary>
    /// Returns all collected metrics in chronological order.
    /// </summary>
    public IReadOnlyCollection<ProcessMetrics> GetCollectedMetrics() => _ctx.CollectedMetrics
        .OrderBy(m => m.Timestamp)
        .ToList()
        .AsReadOnly();

    // -- Lifecycle (thin orchestration only) ------------------------------------------

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ProcessMonitor BackgroundService started, waiting for StartMonitoringAsync() call...");

        // Wait for StartMonitoring() to provide process ID
        try
        {
            await _ctx.StartSignal.Task.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "ProcessMonitor stopped before StartMonitoringAsync() was called");
            return;
        }

        // Delegate all sampling logic to static operations
        await MonitoringOperations.RunSamplingLoopAsync(
            _ctx,
            _processId!,
            _samplingInterval,
            _reportProgress!,
            _logger,
            stoppingToken);
    }
}
