using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Npgsql;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit.Abstractions;

namespace PerformanceTester.IntegrationTesting;

/// <summary>
/// Manages container lifecycle as a singleton.
/// Ensures containers are started once and reused across all tests.
/// Thread-safe using double-check locking pattern.
/// Containers are NEVER stopped or disposed - they remain running for debugging.
/// On subsequent test runs, existing containers are detected and reused (no duplicates created).
/// All containers are grouped in a shared Docker network for better organization and inter-container communication.
///
/// Container access:
///   - PostgreSQL: localhost:54320 (testuser/testpass, db: testdb)
///   - RabbitMQ: localhost:56720 (testuser/testpass)
///   - RabbitMQ Management UI: http://localhost:15673 (testuser/testpass)
///
/// Manual cleanup commands:
///   docker rm -f performance-tester-postgres performance-tester-rabbitmq
///   docker network rm performance-tester-testcontainers-network
///   docker volume rm performance-tester-postgres-testcontainers-data performance-tester-rabbitmq-testcontainers-data
/// </summary>
internal sealed class ContainerManager
{
    private static readonly Lazy<ContainerManager> s_instance = new(() => new ContainerManager());

    public static ContainerManager Instance => s_instance.Value;

    private INetwork? _network;
    private PostgreSqlContainer? _postgresContainer;
    private RabbitMqContainer? _rabbitMqContainer;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private bool _started = false;

    // Connection strings and ports populated after startup
    public string PostgresConnectionString { get; private set; } = string.Empty;
    public string RabbitMqConnectionString { get; private set; } = string.Empty;
    public int RabbitMqManagementPort { get; private set; }

    private ContainerManager() { } // Private constructor for singleton

    /// <summary>
    /// Ensures containers are started (idempotent).
    /// Thread-safe, only starts containers once.
    /// Includes health checks to verify containers are ready to accept connections.
    /// </summary>
    public async Task EnsureStartedAsync(ITestOutputHelper? output = null, CancellationToken ct = default)
    {
        if (_started)
        {
            output?.WriteLine("Containers already started (reusing existing containers)");
            return;
        }

        await _startLock.WaitAsync(ct);
        try
        {
            if (_started)
            {
                output?.WriteLine("Containers already started (reusing existing containers)");
                return;
            }

            await StartContainersAsync(output, ct);
            _started = true;
        }
        finally
        {
            _startLock.Release();
        }
    }

    private async Task StartContainersAsync(ITestOutputHelper? output, CancellationToken ct)
    {
        try
        {
            output?.WriteLine("Creating Docker network 'performance-tester-testcontainers-network'...");
            // Create a shared Docker network to group containers together
            // This makes them appear as a group in Docker Desktop and allows container-to-container communication
            _network = new NetworkBuilder()
                .WithName("performance-tester-testcontainers-network")
                .WithReuse(true) // Reuse network across test runs
                .WithCleanUp(false) // Never remove the network
                .Build();

            await _network.CreateAsync(ct);
            output?.WriteLine("✓ Docker network created");

            output?.WriteLine("Configuring PostgreSQL container (postgres:16-alpine)...");
            // Build PostgreSQL container with reuse enabled
            // WithReuse(true) keeps the container running after tests complete
            // Container will be reused on subsequent test runs
            // Using fixed ports for consistency across test runs
            _postgresContainer = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("testdb")
                .WithUsername("testuser")
                .WithPassword("testpass")
                .WithName("performance-tester-postgres") // Consistent name for reuse
                .WithLabel("com.docker.compose.project", "performance-tester-testcontainers") // Group in Docker Desktop
                .WithLabel("com.docker.compose.service", "postgres") // Service name for grouping
                .WithNetwork(_network) // Add to shared network
                .WithPortBinding(20000, 5432) // Fixed host port for reuse
                .WithVolumeMount("performance-tester-postgres-testcontainers-data", "/var/lib/postgresql/data") // Named volume for data persistence
                .WithReuse(true) // Keep container running and reuse it
                .WithCleanUp(false) // Never remove the container
                .Build();

            output?.WriteLine("Configuring RabbitMQ container (rabbitmq:3.13-management-alpine)...");
            // Build RabbitMQ container with reuse enabled
            // Using fixed ports for consistency across test runs
            _rabbitMqContainer = new RabbitMqBuilder()
                .WithImage("rabbitmq:3.13-management-alpine")
                .WithUsername("testuser")
                .WithPassword("testpass")
                .WithName("performance-tester-rabbitmq") // Consistent name for reuse
                .WithLabel("com.docker.compose.project", "performance-tester-testcontainers") // Group in Docker Desktop
                .WithLabel("com.docker.compose.service", "rabbitmq") // Service name for grouping
                .WithNetwork(_network) // Add to shared network
                .WithPortBinding(20001, 5672)  // Fixed host port for AMQP
                .WithPortBinding(20002, 15672) // Fixed host port for Management UI
                .WithVolumeMount("performance-tester-rabbitmq-testcontainers-data", "/var/lib/rabbitmq") // Named volume for data persistence
                .WithReuse(true) // Keep container running and reuse it
                .WithCleanUp(false) // Never remove the container
                .Build();

            output?.WriteLine("Starting containers in parallel...");
            // Start both containers in parallel for faster initialization
            // If containers already exist and are running, this will reuse them
            await Task.WhenAll(
                _postgresContainer.StartAsync(ct),
                _rabbitMqContainer.StartAsync(ct)
            );
            output?.WriteLine("✓ Containers started");

            // Store connection strings and ports
            PostgresConnectionString = _postgresContainer.GetConnectionString();
            RabbitMqConnectionString = _rabbitMqContainer.GetConnectionString();
            RabbitMqManagementPort = 20002; // Fixed host port for Management UI (see line 112)

            output?.WriteLine($"PostgreSQL: {PostgresConnectionString}");
            output?.WriteLine($"RabbitMQ: {RabbitMqConnectionString}");
            output?.WriteLine($"RabbitMQ Management: http://localhost:{RabbitMqManagementPort}");

            output?.WriteLine("Waiting for containers to be ready...");
            // Wait for containers to be ready to accept connections
            var healthCheckStopwatch = Stopwatch.StartNew();
            await Task.WhenAll(
                WaitForPostgresReadyAsync(output, ct),
                WaitForRabbitMQReadyAsync(output, ct)
            );
            healthCheckStopwatch.Stop();
            output?.WriteLine($"✓ All containers ready ({healthCheckStopwatch.ElapsedMilliseconds}ms)");
        }
        catch
        {
            output?.WriteLine("✗ Container startup failed");
            // On failure, reset state but don't dispose containers or network
            // (they may be partially working and useful for debugging)
            _network = null;
            _postgresContainer = null;
            _rabbitMqContainer = null;
            PostgresConnectionString = string.Empty;
            RabbitMqConnectionString = string.Empty;

            throw; // Re-throw to caller
        }
    }

    /// <summary>
    /// Waits for PostgreSQL to be ready to accept connections.
    /// Polls connection until successful or timeout (30 seconds).
    /// </summary>
    private async Task WaitForPostgresReadyAsync(ITestOutputHelper? output, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(30);
        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                attempts++;
                using var connection = new NpgsqlConnection(PostgresConnectionString);
                await connection.OpenAsync(ct);
                output?.WriteLine($"✓ PostgreSQL ready (attempts: {attempts}, elapsed: {stopwatch.ElapsedMilliseconds}ms)");
                return; // Success!
            }
            catch
            {
                await Task.Delay(100, ct); // Wait and retry
            }
        }

        throw new TimeoutException(
            "PostgreSQL container did not become ready within 30 seconds");
    }

    /// <summary>
    /// Waits for RabbitMQ to be ready to accept connections.
    /// Polls connection until successful or timeout (30 seconds).
    /// </summary>
    private async Task WaitForRabbitMQReadyAsync(ITestOutputHelper? output, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(30);
        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                attempts++;
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(RabbitMqConnectionString)
                };
                using var connection = await factory.CreateConnectionAsync(ct);
                output?.WriteLine($"✓ RabbitMQ ready (attempts: {attempts}, elapsed: {stopwatch.ElapsedMilliseconds}ms)");
                return; // Success!
            }
            catch
            {
                await Task.Delay(100, ct); // Wait and retry
            }
        }

        throw new TimeoutException(
            "RabbitMQ container did not become ready within 30 seconds");
    }
}
