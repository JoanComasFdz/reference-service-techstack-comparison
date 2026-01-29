using PerformanceTester.IntegrationTesting;

namespace PerformanceTester.SystemMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// System under test for SystemMonitoring integration tests.
/// </summary>
public sealed class SystemMonitoringSystem : IntegrationTesting.System
{
    /// <summary>
    /// SystemMonitoring facade providing access to all SystemMonitoring services.
    /// </summary>
    public SystemMonitoringFacade SystemMonitoring { get; private set; } = null!;

    /// <summary>
    /// Creates SystemMonitoring facade.
    /// </summary>
    public void CreateSystemMonitoring(TimeSpan? samplingInterval = null)
    {
        SystemMonitoring = new SystemMonitoringFacade(samplingInterval, base.Output);
    }

    public override void Dispose()
    {
        SystemMonitoring?.Dispose();
        base.Dispose();
    }
}
