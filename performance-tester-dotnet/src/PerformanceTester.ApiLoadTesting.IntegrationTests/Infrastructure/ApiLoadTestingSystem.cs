using PerformanceTester.IntegrationTesting;

namespace PerformanceTester.ApiLoadTesting.IntegrationTests.Infrastructure;

/// <summary>
/// System under test for ApiLoadTesting integration tests.
/// Extends base System class and adds ApiLoadTesting facade for accessing production services.
/// </summary>
public sealed class ApiLoadTestingSystem : IntegrationTesting.System
{
    /// <summary>
    /// ApiLoadTesting facade providing access to all ApiLoadTesting services via DI.
    /// Accessed as: System.ApiLoadTesting.LoadTester
    /// </summary>
    public ApiLoadTesting ApiLoadTesting { get; private set; } = null!;

    /// <summary>
    /// Initializes ApiLoadTesting facade.
    /// Called automatically by xUnit before each test.
    /// </summary>
    protected override async Task InitializeSystemAsync()
    {
        await base.InitializeSystemAsync();
        ApiLoadTesting = new ApiLoadTesting(base.Output);
    }

    public override void Dispose()
    {
        ApiLoadTesting?.Dispose();
        base.Dispose();
    }
}
