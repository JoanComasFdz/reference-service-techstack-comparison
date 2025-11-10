using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.IntegrationTesting.Logging;
using Xunit.Abstractions;

namespace PerformanceTester.ApiLoadTesting.IntegrationTests.Infrastructure;

/// <summary>
/// Facade class that wraps dependency injection and exposes ApiLoadTesting services for testing.
/// This class encapsulates all DI setup logic and provides easy access to production services.
/// Simple pattern - no BackgroundService lifecycle management needed.
/// </summary>
public sealed class ApiLoadTesting : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    /// <summary>
    /// API load tester for running k6 tests.
    /// </summary>
    public IApiLoadTester LoadTester { get; }

    public ApiLoadTesting(ITestOutputHelper? output = null)
    {
        var services = new ServiceCollection();

        // Register ApiLoadTesting production services
        services.AddApiLoadTesting();

        // Wire up xUnit test output logging if provided
        if (output != null)
        {
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddXunitOutput(output);
                builder.SetMinimumLevel(LogLevel.Debug);
            });
        }

        _serviceProvider = services.BuildServiceProvider();

        // Resolve services from DI container
        LoadTester = _serviceProvider.GetRequiredService<IApiLoadTester>();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}
