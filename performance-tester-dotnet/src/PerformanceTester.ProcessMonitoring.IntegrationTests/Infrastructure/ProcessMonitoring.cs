using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.IntegrationTesting.Logging;
using Xunit.Abstractions;

namespace PerformanceTester.ProcessMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Facade class that wraps IHost and exposes ProcessMonitoring services for testing.
/// This class encapsulates all DI setup logic and provides easy access to production services.
/// Manages BackgroundService lifecycle (Start/Stop).
/// </summary>
public sealed class ProcessMonitoring : IAsyncDisposable
{
    private IHost? _host;

    /// <summary>
    /// Process monitor for accessing collected metrics after test completion.
    /// </summary>
    public IProcessMonitor Monitor { get; private set; } = null!;

    public ProcessMonitoring(
        int processId,
        TimeSpan? samplingInterval = null,
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

        // Register ProcessMonitoring production services (includes BackgroundServices)
        builder.Services.AddProcessMonitoring(processId, samplingInterval);

        // Configure HostOptions for graceful shutdown
        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(60);
        });

        _host = builder.Build();

        // Resolve services from DI container
        Monitor = _host.Services.GetRequiredService<IProcessMonitor>();
    }

    /// <summary>
    /// Starts all BackgroundServices (ProcessMonitorService, MetricsCollectorService).
    /// Must be called before monitoring begins.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_host == null) throw new InvalidOperationException("Host not initialized");
        await _host.StartAsync(cancellationToken);
    }

    /// <summary>
    /// Stops all BackgroundServices gracefully.
    /// Call this after test completion to allow metrics collection to complete.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_host == null) return;
        await _host.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
            _host = null;
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
