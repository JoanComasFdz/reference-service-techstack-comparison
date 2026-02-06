using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.Common;
using PerformanceTester.Infrastructure.Database;
using PerformanceTester.Infrastructure.ProcessFinding;
using PerformanceTester.Infrastructure.RabbitMQ;

namespace PerformanceTester.Infrastructure;

/// <summary>
/// Extension methods for registering Infrastructure services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Infrastructure services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="postgresConnectionString">PostgreSQL connection string.</param>
    /// <param name="rabbitMqConnectionString">RabbitMQ AMQP connection string (format: amqp://user:password@host:port).</param>
    /// <param name="rabbitMqManagementPort">Optional RabbitMQ Management API port. If not specified, infers from AMQP port.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string postgresConnectionString,
        string rabbitMqConnectionString,
        int? rabbitMqManagementPort = null)
    {
        // Platform detection happens ONCE at startup, not per method call
        var platform = OSPlatformDetector.GetCurrentPlatform();

        switch (platform)
        {
            case SupportedPlatform.Linux:
                services.AddSingleton<IProcessFinder, LinuxProcessFinder>();
                break;
            case SupportedPlatform.Windows:
                services.AddSingleton<IProcessFinder, WindowsProcessFinder>();
                break;
            default:
                throw new PlatformNotSupportedException(
                    $"Platform {platform} is not supported. Only Linux and Windows are supported.");
        }

        // ServiceDiscovery receives IProcessFinder via constructor injection
        services.AddSingleton<IServiceDiscovery, ServiceDiscovery>();

        // DatabaseCleaner receives connection string and logger
        services.AddSingleton<IDatabase>(sp =>
            new DatabaseCleaner(postgresConnectionString, sp.GetRequiredService<ILogger<DatabaseCleaner>>()));

        // RabbitMqCleaner receives connection string, logger, and optional management port
        services.AddSingleton<IRabbitMQ>(sp =>
            new RabbitMqCleaner(
                rabbitMqConnectionString,
                sp.GetRequiredService<ILogger<RabbitMqCleaner>>(),
                rabbitMqManagementPort));

        return services;
    }
}
