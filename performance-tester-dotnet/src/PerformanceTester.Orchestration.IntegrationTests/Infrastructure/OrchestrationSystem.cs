using Npgsql;
using PerformanceTester.IntegrationTesting;
using PerformanceTester.DockerMonitoring.ValueObjects;
using Xunit.Abstractions;

namespace PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

/// <summary>
/// System under test for Orchestration integration tests.
/// Provides access to:
/// - Orchestration facade (wraps TestOrchestrator)
/// - ConfigurableReferenceService (bi-directional mock service for testing)
/// - IEventConsumer (direct access for testing consumer timeout behavior)
/// - Infrastructure helpers (PostgreSQL, RabbitMQ)
/// </summary>
public sealed class OrchestrationSystem : IntegrationTesting.VhostIsolatedSystem
{
    /// <summary>
    /// Database name used by all orchestration integration tests.
    /// Isolated from production database (dotnet9aot_db) to prevent test data pollution.
    /// </summary>
    public const string IntegrationTestDatabaseName = "dotnet9aot_perftest_integrationtest_db";

    public Orchestration Orchestration { get; private set; } = null!;
    public ConfigurableReferenceService ConfigurableReferenceService { get; private set; } = null!;

    protected override async Task InitializeSystemAsync()
    {
        base.Output?.WriteLine("[INIT] Starting OrchestrationSystem initialization...");
        await base.InitializeSystemAsync();
        base.Output?.WriteLine("[INIT] Base system initialized");

        // NOTE: RabbitMQ connection cleanup is now handled by vhost isolation.
        // Each test gets its own vhost, so cross-test contamination is impossible.
        base.Output?.WriteLine("[INIT] Vhost isolation enabled - skipping connection cleanup");

        // Create integration test database (idempotent - safe to call multiple times)
        base.Output?.WriteLine("[INIT] Creating integration test database...");
        try
        {
            await base.PostgreSQL.CreateTestDatabaseAsync(IntegrationTestDatabaseName);
            base.Output?.WriteLine($"[INIT] ✓ Created integration test database: {IntegrationTestDatabaseName}");
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P04")
        {
            // Database already exists - ignore
            base.Output?.WriteLine($"[INIT] ✓ Integration test database already exists: {IntegrationTestDatabaseName}");
        }

        // Initialize orchestration facade (creates IHost with all services)
        base.Output?.WriteLine("[INIT] Creating Orchestration (IHost)...");
        this.Orchestration = new Orchestration(
            postgresConnectionString: base.PostgreSQL.ConnectionString,
            rabbitMqConnectionString: base.RabbitMQ.ConnectionString,
            rabbitMqContainerName: RabbitMqContainerName.FromString("performance-tester-rabbitmq"),
            postgresContainerName: PostgresContainerName.FromString("performance-tester-postgres"),
            output: base.Output);
        base.Output?.WriteLine("[INIT] ✓ Orchestration (IHost) created");

        // Initialize configurable event publisher (bi-directional mock service)
        // IMPORTANT: Always create a FRESH instance per test to avoid state contamination
        // The service maintains internal state (_receivedInputEventCount, trigger flags, etc.)
        // that cannot be fully reset due to readonly CancellationTokenSource
        base.Output?.WriteLine("[INIT] Creating ConfigurableReferenceService...");

        // Dispose old instance if it exists (from previous test)
        if (this.ConfigurableReferenceService != null)
        {
            base.Output?.WriteLine("[INIT] Disposing old ConfigurableReferenceService instance...");
            await this.ConfigurableReferenceService.DisposeAsync();
        }

        // Create fresh instance with clean state
        this.ConfigurableReferenceService = new ConfigurableReferenceService(
            connectionString: base.RabbitMQ.ConnectionString,
            output: base.Output);
        base.Output?.WriteLine("[INIT] ✓ ConfigurableReferenceService created (fresh instance)");

        base.Output?.WriteLine("[INIT] ✅ OrchestrationSystem initialization complete");
    }

    /// <summary>
    /// Waits for the ConfigurableReferenceService to be fully ready by polling its health check endpoint.
    /// More deterministic than arbitrary delays - ensures RabbitMQ connection and queue bindings are complete.
    /// </summary>
    /// <param name="port">Port where the service is listening</param>
    /// <param name="timeout">Maximum time to wait (default: 10 seconds)</param>
    /// <param name="retryInterval">Time between health check attempts (default: 200ms)</param>
    /// <returns>Task that completes when service is ready</returns>
    /// <exception cref="TimeoutException">Thrown if service doesn't become ready within timeout</exception>
    public async Task WaitForServiceHealthyAsync(
        int port,
        TimeSpan? timeout = null,
        TimeSpan? retryInterval = null)
    {
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(10);
        var actualRetryInterval = retryInterval ?? TimeSpan.FromMilliseconds(200);
        var healthUrl = $"http://localhost:{port}/health";
        var startTime = DateTime.UtcNow;

        Output?.WriteLine($"[HEALTH] Waiting for service health check at {healthUrl} (timeout: {actualTimeout.TotalSeconds}s)...");

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        while (DateTime.UtcNow - startTime < actualTimeout)
        {
            try
            {
                var response = await httpClient.GetAsync(healthUrl);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    Output?.WriteLine($"[HEALTH] ✓ Service ready: {response.StatusCode} - {content}");
                    return;
                }

                Output?.WriteLine($"[HEALTH] Service not ready yet: {response.StatusCode}, retrying in {actualRetryInterval.TotalMilliseconds}ms...");
            }
            catch (HttpRequestException ex)
            {
                Output?.WriteLine($"[HEALTH] Connection failed: {ex.Message}, retrying in {actualRetryInterval.TotalMilliseconds}ms...");
            }
            catch (TaskCanceledException)
            {
                Output?.WriteLine($"[HEALTH] Request timeout, retrying in {actualRetryInterval.TotalMilliseconds}ms...");
            }

            await Task.Delay(actualRetryInterval);
        }

        throw new TimeoutException(
            $"Service health check at {healthUrl} did not return ready status within {actualTimeout.TotalSeconds}s. " +
            "Ensure the service is connected to RabbitMQ and all queue bindings are complete.");
    }

    public override void Dispose()
    {
        // Dispose configurable event publisher (DisposeAsync already calls DisconnectAsync)
        ConfigurableReferenceService?.DisposeAsync().AsTask().GetAwaiter().GetResult();

        // Dispose orchestration (stops monitoring, cleans up IHost)
        Orchestration?.Dispose();

        // Call base class disposal
        base.Dispose();
    }
}
