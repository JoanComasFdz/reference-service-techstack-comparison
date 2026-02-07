using Microsoft.Extensions.DependencyInjection;
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
        // Uses OperatingSystem checks for CA1416 analyzer compatibility
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ISystemInfoDetector, WindowsSystemInfoDetector>();
        }
        else if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<ISystemInfoDetector, LinuxSystemInfoDetector>();
        }
        else
        {
            throw new PlatformNotSupportedException(
                "Only Linux and Windows are supported.");
        }

        // Register report generators
        services.AddSingleton<ReportGenerator>();
        services.AddSingleton<ComparisonReportGenerator>();

        return services;
    }
}
