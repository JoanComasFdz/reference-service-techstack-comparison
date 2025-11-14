using PerformanceTester.IntegrationTesting;

namespace PerformanceTester.Reporting.IntegrationTests.Infrastructure;

/// <summary>
/// System under test for Reporting integration tests.
/// Extends base System class and provides access to Reporting services via DI.
/// </summary>
public sealed class ReportingSystem : IntegrationTesting.System
{
    /// <summary>
    /// Reporting facade providing access to all Reporting services via DI.
    /// Accessed as: System.Reporting.SystemInfoDetector, System.Reporting.ReportGenerator, etc.
    /// </summary>
    public Reporting Reporting { get; private set; } = null!;

    /// <summary>
    /// File system helper for creating and cleaning up temporary directories.
    /// </summary>
    public FileSystem FileSystem { get; private set; } = null!;

    protected override void InitializeSystem()
    {
        base.InitializeSystem();

        // Initialize Reporting facade (includes all DI setup)
        Reporting = new Reporting(base.Output);

        // Initialize file system helper
        FileSystem = new FileSystem();
    }

    public override void Dispose()
    {
        Reporting?.Dispose();
        base.Dispose();
    }
}
