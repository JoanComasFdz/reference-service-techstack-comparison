using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.IntegrationTesting.Logging;
using Xunit.Abstractions;

namespace PerformanceTester.SystemMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Facade wrapping IHost and exposing SystemMonitoring services for testing.
/// </summary>
public sealed class SystemMonitoringFacade : IAsyncDisposable, IDisposable
{
    private IHost? _host;

    /// <summary>
    /// System monitor for accessing collected metrics.
    /// </summary>
    public ISystemMonitor Monitor { get; private set; } = null!;

    public SystemMonitoringFacade(
        TimeSpan? samplingInterval = null,
        ITestOutputHelper? output = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        if (output != null)
        {
            builder.Logging.AddXunitOutput(output);
            builder.Logging.SetMinimumLevel(LogLevel.Debug);
        }

        builder.Services.AddSystemMonitoring(samplingInterval);

        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
        });

        _host = builder.Build();
        Monitor = _host.Services.GetRequiredService<ISystemMonitor>();
    }

    /// <summary>
    /// Starts BackgroundServices.
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
    /// Stops BackgroundServices gracefully.
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
