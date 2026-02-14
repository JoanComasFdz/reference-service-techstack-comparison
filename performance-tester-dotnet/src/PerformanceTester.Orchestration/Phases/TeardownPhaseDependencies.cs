using JoanComasFdz.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.EventPublishing;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Builds a <see cref="Teardown"/> delegate from DI-resolved interfaces.
/// The returned delegate accepts a <see cref="CancellationToken"/> so the caller
/// can control cancellation policy (cancellable for normal flow, non-cancellable for cleanup).
/// </summary>
internal static class TeardownPhaseDependencies
{
    /// <summary>
    /// Runs teardown operations (disconnect publisher, stop monitoring) with caller-supplied cancellation.
    /// </summary>
    public delegate Task<Result<Unit, string>> Teardown(CancellationToken ct);

    public static Teardown Build(IServiceProvider services, ILogger logger)
    {
        var host = services.GetRequiredService<IHost>();
        var eventPublisher = services.GetRequiredService<IEventPublisher>();

        return (ct) => TeardownPhase.ExecuteAsync(
            disconnectEventPublisher: () => eventPublisher.DisconnectAsync(ct),
            stopMonitoring: () => host.StopAsync(ct),
            logger);
    }
}
