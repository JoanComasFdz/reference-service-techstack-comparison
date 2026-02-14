using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.EventPublishing;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Builds the <see cref="RunTeardown"/> and <see cref="CleanupResources"/> delegates
/// from DI-resolved interfaces. Both delegates run the same
/// <see cref="TeardownPhase.ExecuteAsync"/> logic but differ in cancellation behavior:
/// <list type="bullet">
///   <item><see cref="RunTeardown"/> is bound with the active CT (cancellable during normal flow)</item>
///   <item><see cref="CleanupResources"/> is bound with CancellationToken.None (must complete after failure)</item>
/// </list>
/// </summary>
internal static class TeardownPhaseDependencies
{
    public static (RunTeardown RunTeardown, CleanupResources CleanupResources) Build(
        IServiceProvider services,
        ILogger logger,
        CancellationToken ct)
    {
        var host = services.GetRequiredService<IHost>();
        var eventPublisher = services.GetRequiredService<IEventPublisher>();

        RunTeardown runTeardown = () => TeardownPhase.ExecuteAsync(
            disconnectEventPublisher: () => eventPublisher.DisconnectAsync(ct),
            stopMonitoring: () => host.StopAsync(ct),
            logger);

        CleanupResources cleanupResources = async () => await TeardownPhase.ExecuteAsync(
            disconnectEventPublisher: () => eventPublisher.DisconnectAsync(CancellationToken.None),
            stopMonitoring: () => host.StopAsync(CancellationToken.None),
            logger);

        return (runTeardown, cleanupResources);
    }
}
