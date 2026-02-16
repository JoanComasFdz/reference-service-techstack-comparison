using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.EventConsuming;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Builds <see cref="OrchestratorDeps"/> by composing per-phase dependency classes.
/// Resolves shared interfaces and creates operation-level delegates reused across phases.
/// Per-phase interfaces are resolved by each phase's dependency builder.
/// </summary>
internal static class TestOrchestratorDependencies
{
    public static OrchestratorDeps Build(
        IServiceProvider services,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        // Resolve interfaces needed for shared operation-level delegates
        var database = services.GetRequiredService<IDatabase>();
        var rabbitMq = services.GetRequiredService<IRabbitMQ>();
        var eventPublisher = services.GetRequiredService<IEventPublisher>();
        var eventConsumer = services.GetRequiredService<IEventConsumer>();

        // Shared operation-level delegates (reused across phases)
        PhasesToolbox.ClearDatabase clearDatabase = () => database.ClearDatabaseAsync(config.DatabaseName.Value, ct);

        PhasesToolbox.ClearAllQueues clearAllQueues = async () =>
        {
            var result = await rabbitMq.ClearAllQueuesAsync(ct);
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            return result;
        };

        PhasesToolbox.PublishEvents publishEvents = (count) => eventPublisher.PublishEventsAsync(count, ct);

        PhasesToolbox.TrackEvents trackEvents = (count, timeout, progress) => eventConsumer.StartTrackingEventsAsync(count, timeout, progress, ct);

        var teardown = TeardownPhaseDependencies.Build(services, logger);

        return new OrchestratorDeps(
            RunSetup: BuildRunSetup(services, clearDatabase, clearAllQueues, config, logger, ct),
            RunWarmup: BuildRunWarmup(trackEvents, publishEvents, clearDatabase, clearAllQueues, config, logger, ct),
            RunEventTest: EventTestPhaseDependencies.Build(services, trackEvents, publishEvents, config, logger, ct),
            RunApiTest: ApiTestPhaseDependencies.Build(services, config, logger, ct),
            RunTeardown: () => teardown(ct),
            RunReporting: ReportingPhaseDependencies.Build(services, config, logger, ct),
            CleanupResources: () => teardown(CancellationToken.None));
    }

    private static RunSetup BuildRunSetup(
        IServiceProvider services,
        PhasesToolbox.ClearDatabase clearDatabase,
        PhasesToolbox.ClearAllQueues clearAllQueues,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var deps = SetupPhase.BuildDependencies(services, clearDatabase, clearAllQueues, config, ct);
        return (testRunId) => SetupPhase.ExecuteAsync(testRunId, deps, logger);
    }

    private static RunWarmup BuildRunWarmup(
        PhasesToolbox.TrackEvents trackEvents,
        PhasesToolbox.PublishEvents publishEvents,
        PhasesToolbox.ClearDatabase clearDatabase,
        PhasesToolbox.ClearAllQueues clearAllQueues,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var deps = WarmupPhase.BuildDependencies(trackEvents, publishEvents, clearDatabase, clearAllQueues, logger, ct);
        return () => WarmupPhase.ExecuteAsync(config, deps, logger);
    }
}
