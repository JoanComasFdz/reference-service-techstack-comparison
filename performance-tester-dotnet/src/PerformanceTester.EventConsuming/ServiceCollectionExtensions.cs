using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace PerformanceTester.EventConsuming;

/// <summary>
/// Extension methods for registering EventConsuming services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds EventConsuming services to the service collection.
    /// Registers both BackgroundServices (EventConsumerService, MetricsCollectorService) and public interfaces.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="rabbitMqConnectionString">RabbitMQ connection string (format: amqp://user:password@host:port).</param>
    /// <param name="queueName">Name of the queue to consume from (default: "performancetesterdotnet").</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method registers:
    /// - Channel&lt;ThroughputSample&gt; (singleton) - Communication between EventConsumer and MetricsCollector
    /// - EventConsumerService (BackgroundService + IEventConsumer)
    /// - MetricsCollectorService (BackgroundService + IMetricsCollector)
    ///
    /// BackgroundServices will start automatically when IHost.StartAsync() is called.
    /// </remarks>
    public static IServiceCollection AddEventConsuming(
        this IServiceCollection services,
        string rabbitMqConnectionString,
        string queueName = "performancetesterdotnet")
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(rabbitMqConnectionString))
            throw new ArgumentException("RabbitMQ connection string cannot be null or empty", nameof(rabbitMqConnectionString));
        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException("Queue name cannot be null or empty", nameof(queueName));

        // Register channel for throughput samples (singleton - shared between services)
        services.AddSingleton(Channel.CreateUnbounded<ThroughputSample>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        }));

        // Register EventConsumerService as both HostedService and IEventConsumer
        services.AddSingleton<EventConsumerService>(sp =>
        {
            var channel = sp.GetRequiredService<Channel<ThroughputSample>>();
            var logger = sp.GetRequiredService<ILogger<EventConsumerService>>();
            return new EventConsumerService(rabbitMqConnectionString, queueName, channel, logger);
        });
        services.AddHostedService<EventConsumerService>(sp => sp.GetRequiredService<EventConsumerService>());
        services.AddSingleton<IEventConsumer>(sp => sp.GetRequiredService<EventConsumerService>());

        // Register MetricsCollectorService as both HostedService and IMetricsCollector
        services.AddSingleton<MetricsCollectorService>();
        services.AddHostedService<MetricsCollectorService>(sp => sp.GetRequiredService<MetricsCollectorService>());
        services.AddSingleton<IMetricsCollector>(sp => sp.GetRequiredService<MetricsCollectorService>());

        return services;
    }
}
