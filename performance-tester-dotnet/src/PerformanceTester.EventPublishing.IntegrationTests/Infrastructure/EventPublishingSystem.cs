using PerformanceTester.IntegrationTesting;

namespace PerformanceTester.EventPublishing.IntegrationTests.Infrastructure;

/// <summary>
/// System under test for EventPublishing integration tests.
/// Extends base System class and adds EventPublishing facade for accessing production services.
/// </summary>
public sealed class EventPublishingSystem : IntegrationTesting.VhostIsolatedSystem
{
    /// <summary>
    /// EventPublishing facade providing access to all EventPublishing services via DI.
    /// Accessed as: System.EventPublishing.Publisher
    /// </summary>
    public EventPublishing EventPublishing { get; private set; } = null!;

    protected override async Task InitializeSystemAsync()
    {
        await base.InitializeSystemAsync();

        // Create EventPublishing facade with connection details from base System
        this.EventPublishing = new EventPublishing(
            base.RabbitMQ.ConnectionString,
            base.Output);
    }

    public override void Dispose()
    {
        EventPublishing?.Dispose();
        base.Dispose();
    }
}
