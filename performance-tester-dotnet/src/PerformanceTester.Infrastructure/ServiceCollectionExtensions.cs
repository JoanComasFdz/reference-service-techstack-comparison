using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.Database;
using PerformanceTester.Infrastructure.ProcessFinding;
using PerformanceTester.Infrastructure.RabbitMQ;
using PerformanceTester.Infrastructure.ValueObjects;

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
        Port? rabbitMqManagementPort = null)
    {
        // Platform detection determines which static finder to use (Guideline 02-03: delegates for internal wiring)
        var platformResult = OSPlatformDetector.GetCurrentPlatform();

        // Unsupported platform is a deployment error — the app cannot function without a process finder
        if (platformResult.IsFailure)
        {
            throw new PlatformNotSupportedException(platformResult.FailureError);
        }

        var platform = platformResult.SuccessValue;

        // No adapter class — the closure IS the implementation (Guideline 02-01)
        services.AddSingleton<FindServiceProcessIdDelegate>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var discoveryLogger = loggerFactory.CreateLogger(
                typeof(ServiceDiscovery).FullName!);

            var finderLogger = platform.Match(
                linux: _ => loggerFactory.CreateLogger(typeof(LinuxProcessFinder).FullName!),
                windows: _ => loggerFactory.CreateLogger(typeof(WindowsProcessFinder).FullName!));

            var findProcessOnPort = platform.Match(
                linux: _ => (FindProcessOnPortDelegate)((p, ct) => LinuxProcessFinder.FindProcessOnPortAsync(p, finderLogger, ct)),
                windows: _ => (FindProcessOnPortDelegate)((p, ct) => WindowsProcessFinder.FindProcessOnPortAsync(p, finderLogger, ct)));

            return (port, timeout, ct) => ServiceDiscovery.FindServiceProcessIdAsync(
                port,
                timeout,
                findProcessOnPort,
                discoveryLogger,
                ct);
        });

        // No adapter class — the closure IS the implementation (Guidelines 1, 2, 12)
        services.AddSingleton<ClearDatabaseDelegate>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(DatabaseCleaner).FullName!);
            return (databaseName, ct) =>
                DatabaseCleaner.ClearDatabaseAsync(
                    databaseName,
                    postgresConnectionString,
                    logger,
                    ct);
        });

        // No adapter class — the closure IS the implementation (Guidelines 1, 2, 12)
        services.AddSingleton<ClearAllQueuesDelegate>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(RabbitMqCleaner).FullName!);
            return (ct) =>
                RabbitMqCleaner.ClearAllQueuesAsync(
                    rabbitMqConnectionString,
                    rabbitMqManagementPort,
                    logger,
                    ct);
        });

        return services;
    }
}
