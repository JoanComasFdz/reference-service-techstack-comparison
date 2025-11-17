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
    /// <param name="samplingInterval">Sampling interval (default: 500ms).</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method registers ProcessMonitorService with triple registration:
    /// - As singleton (concrete type) - DI container creates and manages instance
    /// - As IHostedService - IHost calls Start/StopAsync lifecycle methods
    /// - As IProcessMonitor - Provides public API for retrieving collected metrics
    ///
    /// BackgroundService will start automatically when IHost.StartAsync() is called,
    /// but will wait for IProcessMonitor.StartMonitoring(processId) to be called before
    /// beginning process monitoring. This deferred start pattern is necessary for orchestration
    /// scenarios where the process ID is not known at DI registration time.
    ///
    /// Usage:
    /// 1. builder.Services.AddProcessMonitoring();
    /// 2. var host = builder.Build();
    /// 3. await host.StartAsync();  // BackgroundService starts but waits
    /// 4. var monitor = host.Services.GetRequiredService&lt;IProcessMonitor&gt;();
    /// 5. monitor.StartMonitoring(processId);  // Now monitoring begins
    /// </remarks>
    public static IServiceCollection AddProcessMonitoring(
        this IServiceCollection services,
        TimeSpan? samplingInterval = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        var interval = samplingInterval ?? TimeSpan.FromMilliseconds(500);

        // Register ProcessMonitorService as singleton (concrete type)
        services.AddSingleton<ProcessMonitorService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ProcessMonitorService>>();
            return new ProcessMonitorService(interval, logger);
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
