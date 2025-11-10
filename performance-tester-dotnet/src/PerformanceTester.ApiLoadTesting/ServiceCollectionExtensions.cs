using Microsoft.Extensions.DependencyInjection;

namespace PerformanceTester.ApiLoadTesting;

/// <summary>
/// Extension methods for registering ApiLoadTesting services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds ApiLoadTesting services to the service collection.
    /// Registers IApiLoadTester as a singleton.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method registers:
    /// - ApiLoadTestService as IApiLoadTester (transient)
    ///
    /// No BackgroundService pattern needed - tests execute synchronously via StartTestAsync().
    /// </remarks>
    public static IServiceCollection AddApiLoadTesting(this IServiceCollection services)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        // Register ApiLoadTestService as IApiLoadTester (transient)
        services.AddTransient<IApiLoadTester, ApiLoadTestService>();

        return services;
    }
}
