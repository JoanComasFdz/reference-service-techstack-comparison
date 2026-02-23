using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.ProcessMonitoring.Internal;

namespace PerformanceTester.ProcessMonitoring;

/// <summary>
/// Extension methods for registering ProcessMonitoring services with dependency injection.
/// Registers internal BackgroundService and exposes named delegates publicly.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds process monitoring services.
    /// Registers an internal BackgroundService and two public named delegates:
    /// <see cref="StartProcessMonitoringDelegate"/> and <see cref="GetProcessMetricsDelegate"/>.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="samplingInterval">Sampling interval (default: 500ms).</param>
    /// <returns>Service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The BackgroundService starts automatically when IHost.StartAsync() is called but
    /// waits for <see cref="StartProcessMonitoringDelegate"/> to be invoked with a process ID.
    /// This deferred start pattern supports orchestration scenarios where the process ID
    /// is not known at DI registration time.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddProcessMonitoring(
        this IServiceCollection services,
        TimeSpan? samplingInterval = null)
    {
        var interval = samplingInterval ?? TimeSpan.FromMilliseconds(500);

        // Register BackgroundService (internal, not exposed)
        services.AddSingleton<ProcessMonitorBackgroundService>(sp =>
            new ProcessMonitorBackgroundService(
                interval,
                sp.GetRequiredService<ILogger<ProcessMonitorBackgroundService>>()));

        services.AddSingleton<IHostedService>(sp =>
            sp.GetRequiredService<ProcessMonitorBackgroundService>());

        // Register public named delegates (standalone in PerformanceTester.ProcessMonitoring namespace)
        services.AddSingleton<StartProcessMonitoringDelegate>(sp =>
        {
            var monitor = sp.GetRequiredService<ProcessMonitorBackgroundService>();
            return monitor.StartMonitoringAsync;
        });

        services.AddSingleton<GetProcessMetricsDelegate>(sp =>
        {
            var monitor = sp.GetRequiredService<ProcessMonitorBackgroundService>();
            return monitor.GetCollectedMetrics;
        });

        return services;
    }
}
