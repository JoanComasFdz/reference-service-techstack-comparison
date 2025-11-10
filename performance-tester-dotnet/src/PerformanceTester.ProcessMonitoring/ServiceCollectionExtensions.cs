using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.ProcessMonitoring;

/// <summary>
/// Extension methods for registering ProcessMonitoring services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds ProcessMonitoring services to the service collection.
    /// Registers ProcessMonitorService as both BackgroundService and IProcessMonitor.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="processId">Process ID to monitor.</param>
    /// <param name="samplingInterval">Sampling interval (default: 500ms).</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method registers ProcessMonitorService with triple registration:
    /// - As singleton (concrete type) - DI container creates and manages instance
    /// - As IHostedService - IHost calls Start/StopAsync lifecycle methods
    /// - As IProcessMonitor - Provides public API for retrieving collected metrics
    ///
    /// BackgroundService will start automatically when IHost.StartAsync() is called.
    /// </remarks>
    public static IServiceCollection AddProcessMonitoring(
        this IServiceCollection services,
        int processId,
        TimeSpan? samplingInterval = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId), processId, "Process ID must be positive");

        var interval = samplingInterval ?? TimeSpan.FromMilliseconds(500);

        // Register ProcessMonitorService as singleton (concrete type)
        services.AddSingleton<ProcessMonitorService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ProcessMonitorService>>();
            return new ProcessMonitorService(processId, interval, logger);
        });

        // Register as IHostedService (BackgroundService lifecycle)
        services.AddHostedService<ProcessMonitorService>(sp =>
            sp.GetRequiredService<ProcessMonitorService>());

        // Register as IProcessMonitor (public API)
        services.AddSingleton<IProcessMonitor>(sp =>
            sp.GetRequiredService<ProcessMonitorService>());

        return services;
    }
}
