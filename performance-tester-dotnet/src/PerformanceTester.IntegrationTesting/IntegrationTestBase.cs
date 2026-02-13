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
    /// If the system supports vhost isolation, sets up a unique vhost for this test.
    /// Override to add project-specific cleanup logic.
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        Console.WriteLine("[DEBUG] IntegrationTestBase.InitializeAsync() START");
        Output.WriteLine("=== Ensuring containers started ===");
        await s_containers.EnsureStartedAsync(Output);
        Console.WriteLine("[DEBUG] Containers ensured started");

        // If system supports vhost isolation, set it up before initializing
        if (System is VhostIsolatedSystem vhostSystem)
        {
            var testContext = GetTestContext();
            Output.WriteLine($"[VHOST] Setting up vhost for test: {testContext.ClassName}.{testContext.MethodName}");

            await vhostSystem.SetupVhostAsync(
                testContext.ClassName,
                testContext.MethodName,
                s_containers.RabbitMqConnectionString,
                s_containers.RabbitMqManagementPort);

            Output.WriteLine($"[VHOST] Created vhost: {vhostSystem.VhostName}");

            // Initialize with vhost-specific connection string
            var connectionStringWithVhost = RabbitMQ.WithVhost(
                s_containers.RabbitMqConnectionString,
                vhostSystem.VhostName!);

            Console.WriteLine("[DEBUG] Calling System.InitializeAsync() with vhost connection string...");
            await System.InitializeAsync(
                s_containers.PostgresConnectionString,
                connectionStringWithVhost,
                s_containers.RabbitMqManagementPort,
                Output);
        }
        else
        {
            // Legacy behavior for non-vhost systems
            Console.WriteLine("[DEBUG] Calling System.InitializeAsync()...");
            await System.InitializeAsync(
                s_containers.PostgresConnectionString,
                s_containers.RabbitMqConnectionString,
                s_containers.RabbitMqManagementPort,
                Output);
        }

        Console.WriteLine("[DEBUG] System.InitializeAsync() completed");
        Output.WriteLine("=== Containers ready ===");
        Console.WriteLine("[DEBUG] IntegrationTestBase.InitializeAsync() END");
    }

    /// <summary>
    /// Called after each test.
    /// Cleans up vhost if applicable, then disposes the system.
    /// Override to add project-specific cleanup (e.g., dispose System instance).
    /// </summary>
    public virtual async Task DisposeAsync()
    {
        System.Dispose();

        // Clean up vhost after test if applicable
        if (System is VhostIsolatedSystem vhostSystem)
        {
            Output.WriteLine($"[VHOST] Tearing down vhost: {vhostSystem.VhostName}");
            await vhostSystem.TeardownVhostAsync();
            Output.WriteLine("[VHOST] Vhost deleted");
        }
    }

    /// <summary>
    /// Gets the current test context (class name, method name).
    /// Uses reflection on xUnit's ITestOutputHelper to extract test information.
    /// Falls back to type name and unique ID if reflection fails.
    /// </summary>
    private TestContext GetTestContext()
    {
        // Try to get test class and method from xUnit's ITestOutputHelper
        // The TestOutputHelper has an internal 'test' field with test case info
        try
        {
            var outputType = Output.GetType();
            var testField = outputType.GetField("test", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance);
            if (testField != null)
            {
                var test = testField.GetValue(Output);
                if (test != null)
                {
                    // Get the test case's DisplayName which contains class.method
                    var testCaseProp = test.GetType().GetProperty("TestCase");

                    if (testCaseProp != null)
                    {
                        var testCase = testCaseProp.GetValue(test);
                        if (testCase != null)
                        {
                            // Try TestMethod property for method name
                            var testMethodProp = testCase.GetType().GetProperty("TestMethod");
                            if (testMethodProp != null)
                            {
                                var testMethod = testMethodProp.GetValue(testCase);
                                if (testMethod != null)
                                {
                                    // Get Method.Name
                                    var methodProp = testMethod.GetType().GetProperty("Method");
                                    if (methodProp != null)
                                    {
                                        var method = methodProp.GetValue(testMethod);
                                        if (method != null)
                                        {
                                            var nameProp = method.GetType().GetProperty("Name");
                                            if (nameProp != null)
                                            {
                                                var methodName = nameProp.GetValue(method)?.ToString();
                                                if (!string.IsNullOrEmpty(methodName))
                                                {
                                                    // Get class name from TestClass
                                                    var testClassProp = testMethod.GetType().GetProperty("TestClass");
                                                    if (testClassProp != null)
                                                    {
                                                        var testClass = testClassProp.GetValue(testMethod);
                                                        if (testClass != null)
                                                        {
                                                            var classProp = testClass.GetType().GetProperty("Class");
                                                            if (classProp != null)
                                                            {
                                                                var cls = classProp.GetValue(testClass);
                                                                if (cls != null)
                                                                {
                                                                    var clsNameProp = cls.GetType().GetProperty("Name");
                                                                    if (clsNameProp != null)
                                                                    {
                                                                        var className = clsNameProp.GetValue(cls)?.ToString();
                                                                        if (!string.IsNullOrEmpty(className))
                                                                        {
                                                                            // Extract simple class name (without namespace)
                                                                            var simpleClassName = className.Contains('.')
                                                                                ? className[(className.LastIndexOf('.') + 1)..]
                                                                                : className;
                                                                            return new TestContext(simpleClassName, methodName);
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Fall through to fallback
        }

        // Fallback: Use concrete test class name and unique ID
        var fallbackClassName = GetType().Name;
        var uniqueId = Guid.NewGuid().ToString("N")[..8]; // Short unique ID
        return new TestContext(fallbackClassName, uniqueId);
    }

    /// <summary>
    /// Represents test context information.
    /// </summary>
    private sealed record TestContext(string ClassName, string MethodName);
}
