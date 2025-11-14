using Microsoft.Extensions.DependencyInjection;
using PerformanceTester.Reporting.ChartGeneration;
using PerformanceTester.Reporting.ComparisonGeneration;
using PerformanceTester.Reporting.ReportGeneration;
using PerformanceTester.Reporting.SystemInfoDetection;

namespace PerformanceTester.Reporting;

/// <summary>
/// Extension methods for registering Reporting services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Reporting services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Platform detection happens ONCE at startup using Strategy Pattern.
    /// Uses PerformanceTester.Common for OS detection (same pattern as Infrastructure).
    /// </remarks>
    public static IServiceCollection AddReporting(this IServiceCollection services)
    {
        // Platform detection happens ONCE at startup, not per method call
        // Uses PerformanceTester.Common for OS detection (same pattern as Infrastructure)
        var platformDetector = new OSPlatformDetector();
        var platform = platformDetector.GetCurrentPlatform();

        switch (platform)
        {
            case SupportedPlatform.Linux:
                services.AddSingleton<ISystemInfoDetector, LinuxSystemInfoDetector>();
                break;
            case SupportedPlatform.Windows:
                services.AddSingleton<ISystemInfoDetector, WindowsSystemInfoDetector>();
                break;
            default:
                throw new PlatformNotSupportedException(
                    $"Platform {platform} is not supported. Only Linux and Windows are supported.");
        }

        // Register report generators
        services.AddSingleton<IReportGenerator, ReportGenerator>();
        services.AddSingleton<IChartGenerator, ChartGenerator>();
        services.AddSingleton<IComparisonReportGenerator, ComparisonReportGenerator>();

        return services;
    }
}
