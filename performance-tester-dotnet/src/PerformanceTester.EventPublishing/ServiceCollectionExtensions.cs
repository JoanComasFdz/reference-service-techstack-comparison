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

        // Internal dependency — thin shell owning connection state (G34)
        services.AddSingleton<RabbitMqPublisher>(sp => new RabbitMqPublisher(
            connectionString,
            sp.GetRequiredService<ILogger<RabbitMqPublisher>>()));

        // No adapter class — the closure IS the implementation (G12)
        services.AddSingleton<ConnectPublisherDelegate>(sp =>
        {
            var publisher = sp.GetRequiredService<RabbitMqPublisher>();
            return publisher.ConnectAsync;
        });

        services.AddSingleton<DisconnectPublisherDelegate>(sp =>
        {
            var publisher = sp.GetRequiredService<RabbitMqPublisher>();
            return publisher.DisconnectAsync;
        });

        services.AddSingleton<PublishEventsDelegate>(sp =>
        {
            var publisher = sp.GetRequiredService<RabbitMqPublisher>();
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(EventPublisher).FullName!);
            return (count, ct) => EventPublisher.PublishEventsAsync(publisher, count, logger, ct);
        });

        return services;
    }
}
