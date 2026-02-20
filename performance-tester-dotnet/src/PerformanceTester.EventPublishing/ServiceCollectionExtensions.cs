using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.EventPublishing.RabbitMq;

namespace PerformanceTester.EventPublishing;

/// <summary>
/// Extension methods for registering EventPublishing services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds EventPublishing delegates to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="rabbitMqConnectionString">RabbitMQ connection string (format: amqp://user:password@host:port).</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Important: <see cref="ConnectPublisherDelegate"/> must be called before <see cref="PublishEventsDelegate"/>.
    /// Call <see cref="DisconnectPublisherDelegate"/> for graceful shutdown.
    ///
    /// Example usage:
    /// <code>
    /// var host = builder.Build();
    /// var connect = host.Services.GetRequiredService&lt;ConnectPublisherDelegate&gt;();
    /// var publish = host.Services.GetRequiredService&lt;PublishEventsDelegate&gt;();
    /// var disconnect = host.Services.GetRequiredService&lt;DisconnectPublisherDelegate&gt;();
    ///
    /// await connect(cancellationToken);
    /// await publish(1000, cancellationToken);
    /// await disconnect(cancellationToken);
    /// </code>
    /// </remarks>
    public static IServiceCollection AddEventPublishing(
        this IServiceCollection services,
        string rabbitMqConnectionString)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (string.IsNullOrWhiteSpace(rabbitMqConnectionString))
        {
            throw new ArgumentException("RabbitMQ connection string cannot be null or empty", nameof(rabbitMqConnectionString));
        }

        // Wrap string → RabbitMqConnectionString at DI boundary (fail-fast validation)
        var connectionString = new RabbitMqConnectionString(rabbitMqConnectionString);

        // Build publisher module: context created inside, operations closed over it (G30)
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(PublisherOperations).FullName!);
            return PublisherOperations.BuildDependencies(connectionString, logger);
        });

        // Extract public delegates from the module's Dependencies record (G14)
        services.AddSingleton<ConnectPublisherDelegate>(sp =>
            sp.GetRequiredService<PublisherOperations.Dependencies>().Connect);

        services.AddSingleton<DisconnectPublisherDelegate>(sp =>
            sp.GetRequiredService<PublisherOperations.Dependencies>().Disconnect);

        services.AddSingleton<PublishEventsDelegate>(sp =>
        {
            var publishDirect = sp.GetRequiredService<PublisherOperations.Dependencies>().PublishDirect;
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(EventPublisher).FullName!);
            return (count, ct) => EventPublisher.PublishEventsAsync(publishDirect, count, logger, ct);
        });

        return services;
    }
}
