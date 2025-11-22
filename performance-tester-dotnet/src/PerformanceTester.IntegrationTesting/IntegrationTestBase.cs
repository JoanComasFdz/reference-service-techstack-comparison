using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.IntegrationTesting;

/// <summary>
/// Base class for all integration tests.
/// Provides container initialization and logging capture.
/// Each test project inherits and adds project-specific cleanup logic.
/// </summary>
public abstract class IntegrationTestBase<TSystem> : IAsyncLifetime where TSystem : System
{
    private static readonly ContainerManager s_containers = ContainerManager.Instance;

    /// <summary>
    /// xUnit test output helper for capturing logs and diagnostic messages.
    /// </summary>
    protected ITestOutputHelper Output { get; }

    protected TSystem System { get; private set; } 

    protected IntegrationTestBase(ITestOutputHelper output)
    {
        Output = output;
        // Note: Logging configuration is handled by each test project's SystemUnderTest class
        // using LoggingTestExtensions.AddXunitOutput(output)

        System = Activator.CreateInstance<TSystem>();
    }

    /// <summary>
    /// Called before each test.
    /// Ensures containers are started.
    /// Override to add project-specific cleanup logic.
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        Console.WriteLine("[DEBUG] IntegrationTestBase.InitializeAsync() START");
        Output.WriteLine("=== Ensuring containers started ===");
        await s_containers.EnsureStartedAsync(Output);
        Console.WriteLine("[DEBUG] Containers ensured started, calling System.InitializeAsync()...");
        await System.InitializeAsync(
            s_containers.PostgresConnectionString,
            s_containers.RabbitMqConnectionString,
            s_containers.RabbitMqManagementPort,
            Output); // Pass output for logging
        Console.WriteLine("[DEBUG] System.InitializeAsync() completed");
        Output.WriteLine("=== Containers ready ===");
        Console.WriteLine("[DEBUG] IntegrationTestBase.InitializeAsync() END");
    }

    /// <summary>
    /// Called after each test.
    /// Intentionally empty - data persists for debugging.
    /// Override to add project-specific cleanup (e.g., dispose System instance).
    /// </summary>
    public virtual async Task DisposeAsync()
    {
        System.Dispose();
        await Task.CompletedTask;
    }
}
