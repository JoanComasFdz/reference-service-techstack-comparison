using PerformanceTester.IntegrationTesting;

namespace PerformanceTester.Infrastructure.IntegrationTests.Infrastructure;

/// <summary>
/// Extends <see cref="IntegrationTesting.System"/> to offer access to the project under test services.
/// </summary>
public sealed class InfrastructureSystem : IntegrationTesting.VhostIsolatedSystem
{
    public Infrastructure Infrastructure { get; private set; } = null!;

    protected override async Task InitializeSystemAsync()
    {
        await base.InitializeSystemAsync();
        this.Infrastructure = new Infrastructure(
            base.PostgreSQL.ConnectionString,
            base.RabbitMQ.ConnectionString,
            base.RabbitMQ.ManagementPort,
            base.Output); // Use Output from base class
    }

    public override void Dispose()
    {
        Infrastructure?.Dispose();
        base.Dispose();
    }
}
