using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.IntegrationTesting.Logging;
using Xunit.Abstractions;

namespace PerformanceTester.EventPublishing.IntegrationTests.Infrastructure;

/// <summary>
/// Facade class that wraps DI container and exposes EventPublishing delegates for testing.
/// Automatically connects to RabbitMQ during initialization.
/// </summary>
public sealed class EventPublishing : IAsyncDisposable
{
    private IHost? _host;
    private readonly DisconnectPublisherDelegate _disconnectPublisher;

    /// <summary>
    /// Publishes CloudEvents to RabbitMQ.
    /// </summary>
    public PublishEventsDelegate PublishEvents { get; }

    public EventPublishing(
        string rabbitMQConnectionString,
        ITestOutputHelper? output = null)
    {
        var builder = Host.CreateApplicationBuilder();

        // Clear default logging providers
        builder.Logging.ClearProviders();

        // Wire up xUnit test output logging if provided
        if (output != null)
        {
            builder.Logging.AddXunitOutput(output);
            builder.Logging.SetMinimumLevel(LogLevel.Debug);
        }

        // Register EventPublishing production services
        builder.Services.AddEventPublishing(rabbitMQConnectionString);

        _host = builder.Build();

        // Resolve delegates from DI container
        var connectPublisher = _host.Services.GetRequiredService<ConnectPublisherDelegate>();
        _disconnectPublisher = _host.Services.GetRequiredService<DisconnectPublisherDelegate>();
        PublishEvents = _host.Services.GetRequiredService<PublishEventsDelegate>();

        // Explicitly connect to RabbitMQ (synchronous wait is acceptable in test setup)
        connectPublisher().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        await _disconnectPublisher();
        _host?.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
