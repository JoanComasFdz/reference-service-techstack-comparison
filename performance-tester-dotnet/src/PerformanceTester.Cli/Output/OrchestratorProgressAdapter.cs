using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.Orchestration;
using PerformanceTester.Orchestration.ValueObjects;

namespace PerformanceTester.Cli.Output;

/// <summary>
/// Adapts orchestrator PhaseInfo to TestProgress for the progress reporter.
/// Instance class - has mutable tracking state (CODING_GUIDELINES: Static Classes for Pure Logic - this has state).
/// Uses PhaseInfoConverter for pure conversions (CODING_GUIDELINES: Toolbox Pattern).
/// </summary>
public sealed class OrchestratorProgressAdapter
{
    private readonly ProgressReporter _progressReporter;
    private readonly EventCount _totalEventCount;
    private readonly ApiDuration _apiDuration;

    // Mutable tracking state
    private int _currentEventCount;
    private int _apiRequestCount;

    public OrchestratorProgressAdapter(
        ProgressReporter progressReporter,
        EventCount totalEventCount,
        ApiDuration apiDuration)
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
            currentEvents: status == PhaseStatus.Completed ? _totalEventCount.Value : _currentEventCount,
            totalEvents: _totalEventCount.Value,
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
            elapsedSeconds: status == PhaseStatus.Completed ? _apiDuration.Value.TotalSeconds : 0,
            totalSeconds: _apiDuration.Value.TotalSeconds,
            requestCount: _apiRequestCount,
            message: message);
    }
}
