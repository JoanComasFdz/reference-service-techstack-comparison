using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.EventPublishing;
using PerformanceTester.IntegrationTesting;
using PerformanceTester.IntegrationTesting.Logging;

namespace PerformanceTester.EventConsuming.IntegrationTests.Infrastructure;

/// <summary>
/// System under test for EventConsuming integration tests.
/// Extends base System class and adds EventConsuming facade for accessing production services.
/// Also includes EventPublisher for test setup (publishing CloudEvents to consume).
/// </summary>
public sealed class EventConsumingSystem : IntegrationTesting.VhostIsolatedSystem
{
    private IHost? _eventPublisherHost;

    /// <summary>
    /// EventConsuming facade providing access to all EventConsuming services via DI.
    /// Accessed as: System.EventConsuming.Consumer
    /// </summary>
    public EventConsuming EventConsuming { get; private set; } = null!;

    /// <summary>
    /// Publishes CloudEvents to RabbitMQ for test setup.
    /// Accessed as: System.PublishEvents
    /// </summary>
    public PublishEventsDelegate PublishEvents { get; private set; } = null!;

    /// <summary>
    /// Creates an EventConsuming facade with the specified queue name.
    /// Call this in your test setup with a unique queue name for parallel test execution.
    /// </summary>
    public void CreateEventConsuming(string queueName)
    {
        this.EventConsuming = new EventConsuming(
            base.RabbitMQ.ConnectionString,
            queueName: queueName,
            base.Output);
    }

    protected override async Task InitializeSystemAsync()
    {
        await base.InitializeSystemAsync();

        // Create production EventPublisher via DI for test setup
        var builder = Host.CreateApplicationBuilder();

        // Clear default logging providers
        builder.Logging.ClearProviders();

        // Wire up xUnit test output logging if provided
        if (base.Output != null)
        {
            builder.Logging.AddXunitOutput(base.Output);
            builder.Logging.SetMinimumLevel(LogLevel.Debug);
        }

        // Register EventPublishing production services
        builder.Services.AddEventPublishing(base.RabbitMQ.ConnectionString);

        _eventPublisherHost = builder.Build();

        // Resolve delegates from DI container
        var connectPublisher = _eventPublisherHost.Services.GetRequiredService<ConnectPublisherDelegate>();
        this.PublishEvents = _eventPublisherHost.Services.GetRequiredService<PublishEventsDelegate>();

        // Connect to RabbitMQ using delegate
        await connectPublisher();
    }

    public override void Dispose()
    {
        EventConsuming?.Dispose();
        _eventPublisherHost?.Dispose();
        base.Dispose();
    }
}
