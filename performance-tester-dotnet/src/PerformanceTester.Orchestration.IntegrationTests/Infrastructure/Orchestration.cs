using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.EventConsuming;
using PerformanceTester.IntegrationTesting.Logging;
using Xunit.Abstractions;

namespace PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

/// <summary>
/// Facade for accessing ITestOrchestrator and dependencies.
/// Creates IHost with all Phase 1-4 services registered via AddOrchestration().
/// Provides direct access to IEventConsumer for testing consumer timeout behavior.
/// </summary>
public sealed class Orchestration : IDisposable
{
    private IHost? _host;

    public ITestOrchestrator Orchestrator { get; private set; } = null!;

    public Orchestration(
        string postgresConnectionString,
        string rabbitMqConnectionString,
        string rabbitMqContainerName,
        string postgresContainerName,
        ITestOutputHelper? output)
    {
        output?.WriteLine("[ORCH] Creating Orchestration IHost...");

        // Build IHost with all services
        output?.WriteLine("[ORCH] Creating host builder...");
        var builder = Host.CreateApplicationBuilder();
        output?.WriteLine("[ORCH] ✓ Host builder created");

        // Configure logging to test output
        output?.WriteLine("[ORCH] Configuring logging...");
        builder.Logging.ClearProviders();
        if (output != null)
        {
            builder.Logging.AddXunitOutput(output);
        }
        output?.WriteLine("[ORCH] ✓ Logging configured");

        // Single call registers ALL dependencies (Phases 1-4)
        output?.WriteLine("[ORCH] Registering services (AddOrchestration)...");
        builder.Services.AddOrchestration(
            postgresConnectionString: postgresConnectionString,
            rabbitMqConnectionString: rabbitMqConnectionString,
            rabbitMqContainerName: rabbitMqContainerName,
            postgresContainerName: postgresContainerName);
        output?.WriteLine("[ORCH] ✓ Services registered");

        output?.WriteLine("[ORCH] Building IHost...");
        _host = builder.Build();
        output?.WriteLine("[ORCH] ✓ IHost built");

        // Resolve orchestrator
        output?.WriteLine("[ORCH] Resolving ITestOrchestrator...");
        this.Orchestrator = _host.Services.GetRequiredService<ITestOrchestrator>();
        output?.WriteLine("[ORCH] ✓ ITestOrchestrator resolved");

        output?.WriteLine("[ORCH] ✅ Orchestration IHost creation complete");
    }

    /// <summary>
    /// Starts the IHost and all BackgroundServices (including EventConsumerService).
    /// Only needed when testing consumers directly (not via orchestrator).
    /// </summary>
    public async Task StartHostAsync(CancellationToken cancellationToken = default)
    {
        if (_host == null)
        {
            throw new InvalidOperationException("Host not initialized");
        }

        await _host.StartAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the IEventConsumer for direct testing of consumer timeout behavior.
    /// </summary>
    public IEventConsumer GetEventConsumer()
    {
        if (_host == null)
        {
            throw new InvalidOperationException("Host not initialized");
        }

        return _host.Services.GetRequiredService<IEventConsumer>();
    }

    public void Dispose()
    {
        // IMPORTANT: Must call StopAsync before Dispose to allow BackgroundServices
        // (like EventConsumerService) to shut down gracefully and release resources
        // (RabbitMQ connections, channels, semaphores). Without this, tests can hang
        // intermittently due to resource contention between test runs.
        if (_host != null)
        {
            try
            {
                _host.StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // Ignore exceptions during stop - Dispose will clean up anyway
            }
            finally
            {
                _host.Dispose();
            }
        }
    }
}
