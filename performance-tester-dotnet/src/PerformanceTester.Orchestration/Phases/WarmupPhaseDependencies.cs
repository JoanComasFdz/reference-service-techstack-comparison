using Microsoft.Extensions.Logging;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Builds the <see cref="RunWarmup"/> delegate from shared operation delegates.
/// </summary>
internal static class WarmupPhaseDependencies
{
    public static RunWarmup Build(
        PhasesToolbox.TrackEvents trackEvents,
        PhasesToolbox.PublishEvents publishEvents,
        PhasesToolbox.ClearDatabase clearDatabase,
        PhasesToolbox.ClearAllQueues clearAllQueues,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        return () => WarmupPhase.ExecuteAsync(
            config,
            trackEvents: trackEvents,
            publishEvents: publishEvents,
            executeWarmupApiCalls: (url, count) => WarmupPhase.ExecuteWarmupApiCallsAsync(url, count, logger, ct),
            clearDatabase: clearDatabase,
            clearAllQueues: clearAllQueues,
            logger);
    }
}
