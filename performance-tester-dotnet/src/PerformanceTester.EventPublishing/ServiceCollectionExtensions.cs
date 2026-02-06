using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.EventPublishing;

/// <summary>
/// Extension methods for registering EventPublishing services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds EventPublishing services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="rabbitMqConnectionString">RabbitMQ connection string (format: amqp://user:password@host:port).</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Important: IEventPublisher requires explicit connection initialization.
    /// After resolving IEventPublisher from the container, call ConnectAsync()
    /// before publishing events, and DisconnectAsync() for graceful shutdown.
    ///
    /// Example usage:
    /// <code>
    /// var host = builder.Build();
    /// var publisher = host.Services.GetRequiredService&lt;IEventPublisher&gt;();
    ///
    /// await publisher.ConnectAsync(cancellationToken);
    /// await publisher.PublishEventsAsync(1000, cancellationToken);
    /// await publisher.DisconnectAsync(cancellationToken);
    /// </code>
    /// </remarks>
    public static IServiceCollection AddEventPublishing(
        this IServiceCollection services,
        string rabbitMqConnectionString)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(rabbitMqConnectionString))
            throw new ArgumentException("RabbitMQ connection string cannot be null or empty", nameof(rabbitMqConnectionString));

        // Register internal dependencies
        services.AddSingleton<RabbitMqPublisher>(sp => new RabbitMqPublisher(
            rabbitMqConnectionString,
            sp.GetRequiredService<ILogger<RabbitMqPublisher>>()));

        // Register public API
        services.AddSingleton<IEventPublisher, EventPublisher>();

        return services;
    }
}
