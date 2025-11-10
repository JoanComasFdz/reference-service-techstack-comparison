using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.IntegrationTesting.Logging;
using Xunit.Abstractions;

namespace PerformanceTester.EventConsuming.IntegrationTests.Infrastructure;

/// <summary>
/// Facade class that wraps IHost and exposes EventConsuming services for testing.
/// This class encapsulates all DI setup logic and provides easy access to production services.
/// Manages BackgroundService lifecycle (Start/Stop).
/// </summary>
public sealed class EventConsuming : IDisposable
{
    private IHost? _host;

    /// <summary>
    /// Event consumer for consuming CloudEvents from RabbitMQ.
    /// </summary>
    public IEventConsumer Consumer { get; private set; } = null!;

    /// <summary>
    /// Metrics collector for retrieving throughput samples after test completion.
    /// </summary>
    public IMetricsCollector MetricsCollector { get; private set; } = null!;

    public EventConsuming(
        string rabbitMQConnectionString,
        string queueName,
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

        // Register EventConsuming production services (includes BackgroundServices)
        builder.Services.AddEventConsuming(rabbitMQConnectionString, queueName);

        // Configure HostOptions for graceful shutdown
        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(60);
        });

        _host = builder.Build();

        // Resolve services from DI container
        Consumer = _host.Services.GetRequiredService<IEventConsumer>();
        MetricsCollector = _host.Services.GetRequiredService<IMetricsCollector>();
    }

    /// <summary>
    /// Starts all BackgroundServices (EventConsumerService, MetricsCollectorService).
    /// Must be called before using Consumer.
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

    public void Dispose()
    {
        if (_host != null)
        {
            // Proper async disposal with timeout to ensure clean shutdown
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                // Wait for graceful shutdown with timeout
                _host.StopAsync(cts.Token).Wait(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout waiting for graceful shutdown - continue with disposal
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
            {
                // Timeout waiting for graceful shutdown - continue with disposal
            }

            _host.Dispose();
            _host = null;
        }
    }
}
