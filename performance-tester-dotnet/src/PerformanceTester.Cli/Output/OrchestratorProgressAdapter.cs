using PerformanceTester.Orchestration;

namespace PerformanceTester.Cli.Output;

/// <summary>
/// Adapts orchestrator PhaseInfo to TestProgress for the progress reporter.
/// Instance class - has mutable tracking state (CODING_GUIDELINES: Static Classes for Pure Logic - this has state).
/// Uses PhaseInfoConverter for pure conversions (CODING_GUIDELINES: Toolbox Pattern).
/// </summary>
public sealed class OrchestratorProgressAdapter : IProgress<PhaseInfo>
{
    private readonly IProgressReporter _progressReporter;
    private readonly int _totalEventCount;
    private readonly TimeSpan _apiDuration;

    // Mutable tracking state
    private int _currentEventCount;
    private DateTime _apiStartTime;
    private int _apiRequestCount;

    public OrchestratorProgressAdapter(
        IProgressReporter progressReporter,
        int totalEventCount,
        TimeSpan apiDuration)
    {
        _progressReporter = progressReporter ?? throw new ArgumentNullException(nameof(progressReporter));
        _totalEventCount = totalEventCount;
        _apiDuration = apiDuration;
    }

    public void Report(PhaseInfo value)
    {
        var status = PhaseInfoConverter.ConvertState(value.State);

        // Convert to TestProgress - explicit switch, no hidden logic
        var progress = value.Phase switch
        {
            TestPhase.Setup => PhaseInfoConverter.CreateSetupProgress(status, value.Message),

            TestPhase.Warmup => PhaseInfoConverter.CreateWarmupProgress(status, value.Message),

            TestPhase.EventTest => CreateEventProgressFromMessage(status, value.Message),

            TestPhase.ApiTest => CreateApiProgressFromMessage(status, value.Message),

            _ => new TestProgress("Unknown", 0, 4, status, Message: value.Message)
        };

        _progressReporter.ReportProgress(progress);
    }

    private TestProgress CreateEventProgressFromMessage(PhaseStatus status, string? message)
    {
        // Try to parse real-time progress from message
        if (status == PhaseStatus.InProgress)
        {
            var parsed = ProgressMessageParser.TryParseEventProgress(message);
            if (parsed.HasValue)
            {
                _currentEventCount = parsed.Value.Current;
                return PhaseInfoConverter.CreateEventProgress(
                    status,
                    currentEvents: parsed.Value.Current,
                    totalEvents: parsed.Value.Total);
            }
        }

        // Fallback to stored values
        return PhaseInfoConverter.CreateEventProgress(
            status,
            currentEvents: status == PhaseStatus.Completed ? _totalEventCount : _currentEventCount,
            totalEvents: _totalEventCount,
            message: message);
    }

    private TestProgress CreateApiProgressFromMessage(PhaseStatus status, string? message)
    {
        // Try to parse real-time progress from message
        if (status == PhaseStatus.InProgress)
        {
            var parsed = ProgressMessageParser.TryParseApiProgress(message);
            if (parsed.HasValue)
            {
                _apiRequestCount = parsed.Value.Requests;
                return PhaseInfoConverter.CreateApiProgress(
                    status,
                    elapsedSeconds: parsed.Value.Elapsed,
                    totalSeconds: parsed.Value.Total,
                    requestCount: parsed.Value.Requests);
            }
        }

        // Fallback to stored values
        return PhaseInfoConverter.CreateApiProgress(
            status,
            elapsedSeconds: status == PhaseStatus.Completed
                ? _apiDuration.TotalSeconds
                : (DateTime.UtcNow - _apiStartTime).TotalSeconds,
            totalSeconds: _apiDuration.TotalSeconds,
            requestCount: _apiRequestCount,
            message: message);
    }

    /// <summary>
    /// Updates event count. Called from consumer progress callback.
    /// </summary>
    public void UpdateEventCount(int count)
    {
        _currentEventCount = count;
        _progressReporter.ReportProgress(PhaseInfoConverter.CreateEventProgress(
            PhaseStatus.InProgress,
            currentEvents: count,
            totalEvents: _totalEventCount));
    }

    /// <summary>
    /// Starts API tracking. Called when API test phase begins.
    /// </summary>
    public void StartApiTracking()
    {
        _apiStartTime = DateTime.UtcNow;
        _apiRequestCount = 0;
    }

    /// <summary>
    /// Updates API progress. Called from k6 progress callback.
    /// </summary>
    public void UpdateApiProgress(int requestCount)
    {
        _apiRequestCount = requestCount;
        var elapsed = (DateTime.UtcNow - _apiStartTime).TotalSeconds;
        _progressReporter.ReportProgress(PhaseInfoConverter.CreateApiProgress(
            PhaseStatus.InProgress,
            elapsedSeconds: elapsed,
            totalSeconds: _apiDuration.TotalSeconds,
            requestCount: requestCount));
    }
}
