using JoanComasFdz.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.EventConsuming;
using PerformanceTester.IntegrationTesting.Logging;
using PerformanceTester.Reporting;
using Xunit.Abstractions;

namespace PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

/// <summary>
/// Facade for accessing TestOrchestrator.
/// Creates IHost with all Phase 1-3 services registered via AddOrchestration().
/// Wraps TestOrchestratorBuilder.Build() + TestOrchestrator.RunTestAsync() into a single public method.
/// </summary>
public sealed class Orchestration : IDisposable
{
    private IHost? _host;

    /// <summary>
    /// Logger for orchestrator output, configured to write to xUnit test output.
    /// </summary>
    public ILogger Logger { get; private set; } = null!;

    public Orchestration(
        string postgresConnectionString,
        string rabbitMqConnectionString,
        string rabbitMqContainerName,
        string postgresContainerName,
        ITestOutputHelper? output)
    {
        output?.WriteLine("[ORCH] Creating Orchestration IHost...");

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

        // Single call registers ALL dependencies (Phases 1-3)
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

        // Create logger for orchestrator
        this.Logger = _host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(TestOrchestrator).FullName!);

        output?.WriteLine("[ORCH] ✅ Orchestration IHost creation complete");
    }

    /// <summary>
    /// Builds phase delegates and runs the full test orchestration.
    /// Wraps TestOrchestratorBuilder.Build() + TestOrchestrator.RunTestAsync().
    /// </summary>
    public Task<Result<TestReport, TestRunFailure>> RunTestAsync(
        TestConfiguration config,
        IProgress<PhaseInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_host == null)
        {
            throw new InvalidOperationException("Host not initialized");
        }

        var deps = TestOrchestratorBuilder.Build(_host.Services, config, Logger, cancellationToken);
        return TestOrchestrator.RunTestAsync(deps, config, progress, Logger);
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
        if (_host != null)
        {
            try
            {
                _host.StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // Ignore exceptions during stop
            }
            finally
            {
                _host.Dispose();
            }
        }
    }
}
