using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.EventConsuming;
using PerformanceTester.EventPublishing;
using PerformanceTester.Infrastructure;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Builds <see cref="OrchestratorDeps"/> by composing per-phase dependency classes.
/// Resolves shared interfaces and creates operation-level delegates reused across phases.
/// Per-phase interfaces are resolved by each <c>XyzPhaseDependencies.Build()</c>.
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

        PhasesToolbox.TrackEvents trackEvents = (count, timeout, progress) =>
            eventConsumer.StartTrackingEventsAsync(count, timeout, progress, ct);

        var (runTeardown, cleanupResources) = TeardownPhaseDependencies.Build(services, logger, ct);

        return new OrchestratorDeps(
            RunSetup: SetupPhaseDependencies.Build(services, clearDatabase, clearAllQueues, config, logger, ct),
            RunWarmup: WarmupPhaseDependencies.Build(trackEvents, publishEvents, clearDatabase, clearAllQueues, config, logger, ct),
            RunEventTest: EventTestPhaseDependencies.Build(services, trackEvents, publishEvents, config, logger, ct),
            RunApiTest: ApiTestPhaseDependencies.Build(services, config, logger, ct),
            RunTeardown: runTeardown,
            RunReporting: ReportingPhaseDependencies.Build(services, config, logger, ct),
            CleanupResources: cleanupResources);
    }
}
