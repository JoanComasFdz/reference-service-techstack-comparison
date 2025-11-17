using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.IntegrationTesting.Logging;
using Xunit.Abstractions;

namespace PerformanceTester.EventPublishing.IntegrationTests.Infrastructure;

/// <summary>
/// Facade class that wraps DI container and exposes EventPublishing services for testing.
/// This class encapsulates all DI setup logic and provides easy access to production services.
/// Automatically connects to RabbitMQ during initialization.
/// </summary>
public sealed class EventPublishing : IAsyncDisposable
{
    private IHost? _host;

    /// <summary>
    /// Event publisher for publishing CloudEvents to RabbitMQ.
    /// </summary>
    public IEventPublisher Publisher { get; private set; } = null!;

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

        // Resolve services from DI container
        Publisher = _host.Services.GetRequiredService<IEventPublisher>();

        // Explicitly connect to RabbitMQ using public API (synchronous wait is acceptable in test setup)
        Publisher.ConnectAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Publisher != null)
        {
            await Publisher.DisconnectAsync();
        }
        _host?.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
