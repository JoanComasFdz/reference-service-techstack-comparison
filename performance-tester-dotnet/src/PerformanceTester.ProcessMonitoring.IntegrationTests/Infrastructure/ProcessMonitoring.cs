using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;
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
    /// Delegate for starting process monitoring.
    /// </summary>
    public StartProcessMonitoringDelegate StartMonitoring { get; private set; } = null!;

    /// <summary>
    /// Delegate for retrieving collected process metrics.
    /// </summary>
    public GetProcessMetricsDelegate GetMetrics { get; private set; } = null!;

    public ProcessMonitoring(
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
        builder.Services.AddProcessMonitoring(samplingInterval);

        // Configure HostOptions for graceful shutdown
        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(60);
        });

        _host = builder.Build();

        // Resolve delegates from DI container
        StartMonitoring = _host.Services.GetRequiredService<StartProcessMonitoringDelegate>();
        GetMetrics = _host.Services.GetRequiredService<GetProcessMetricsDelegate>();
    }

    /// <summary>
    /// Starts all BackgroundServices (ProcessMonitorService).
    /// BackgroundService will wait for StartMonitoringAsync() to be called before beginning monitoring.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_host == null)
        {
            throw new InvalidOperationException("Host not initialized");
        }

        await _host.StartAsync(cancellationToken);
    }

    /// <summary>
    /// Stops all BackgroundServices gracefully.
    /// Call this after test completion to allow metrics collection to complete.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_host == null)
        {
            return;
        }

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
