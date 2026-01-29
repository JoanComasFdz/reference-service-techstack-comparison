using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.SystemMonitoring;

/// <summary>
/// Extension methods for registering SystemMonitoring services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Default sampling interval for system monitoring (500ms, matches Python).
    /// </summary>
    public static readonly TimeSpan DefaultSamplingInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Adds SystemMonitoring services to the service collection.
    /// Registers BackgroundService (SystemMonitorService) and public interface.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="samplingInterval">Sampling interval (default: 500ms).</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method registers:
    /// - SystemMonitorService (BackgroundService + ISystemMonitor)
    ///
    /// BackgroundService will start when IHost.StartAsync() is called.
    /// Actual monitoring begins when ISystemMonitor.StartMonitoringAsync() is called.
    /// </remarks>
    public static IServiceCollection AddSystemMonitoring(
        this IServiceCollection services,
        TimeSpan? samplingInterval = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var interval = samplingInterval ?? DefaultSamplingInterval;

        // Register SystemMonitorService as both BackgroundService and ISystemMonitor
        services.AddSingleton<SystemMonitorService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SystemMonitorService>>();
            return new SystemMonitorService(interval, logger);
        });

        services.AddHostedService<SystemMonitorService>(sp => sp.GetRequiredService<SystemMonitorService>());
        services.AddSingleton<ISystemMonitor>(sp => sp.GetRequiredService<SystemMonitorService>());

        return services;
    }
}
