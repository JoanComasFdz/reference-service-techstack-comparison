# PerformanceTester.Orchestration

**Phase 4: Test Orchestration**

Orchestrates complete performance test workflows from infrastructure setup through report generation.

## Overview

The Orchestration slice coordinates all phases of performance testing:
- Infrastructure setup (database, RabbitMQ cleanup)
- Service discovery (finding process listening on port)
- Warmup phase (JIT compilation, connection pool initialization)
- Event throughput testing (concurrent publish/consume)
- API load testing (k6-based HTTP benchmarking)
- Metrics collection (process, Docker, throughput monitoring)
- Report generation (JSON reports + PNG charts)

## API

### TestOrchestrator (Static Module)

```csharp
internal static class TestOrchestrator
{
    // Delegate definitions (RunSetup, RunWarmup, RunEventTest, etc.)

    public record Dependencies(
        RunSetup RunSetup, RunWarmup RunWarmup, RunEventTest RunEventTest,
        RunApiTest RunApiTest, RunTeardown RunTeardown,
        RunReporting RunReporting, CleanupResources CleanupResources);

    public static Dependencies BuildDependencies(
        IServiceProvider services, TestConfiguration config,
        ILogger logger, CancellationToken ct);

    public static Task<Result<TestReport, TestRunFailure>> RunTestAsync(
        Dependencies deps, TestConfiguration configuration,
        IProgress<PhaseInfo>? progress, ILogger logger);
}
```

### TestConfiguration

```csharp
public record TestConfiguration(
    int EventCount = 10000,                  // Number of events to publish/consume
    TimeSpan? ApiDuration = null,            // API test duration (default: 30s)
    int ApiWorkers = 1,                      // Concurrent API workers
    TimeSpan? InactivityTimeout = null,      // Consumer timeout (default: 120s)
    int ServicePort = 8080,                  // Service port to test
    string DatabaseName = "defaultdb",       // PostgreSQL database
    string ResultsFolder = "./test-results", // Output directory
    string RabbitMqContainerName = "performancetest-rabbitmq",
    string PostgresContainerName = "performancetest-postgres");
```

## Workflow Phases

### Phase 0: Setup (ExecuteSetupPhaseAsync)

1. **Service Discovery** - Find process listening on configured port (30s timeout)
2. **Start Monitoring** - Start all BackgroundServices via IHost.StartAsync
3. **Clear Database** - Truncate all tables in configured database
4. **Clear RabbitMQ** - Purge all queues in default vhost

**Outcome:** Clean infrastructure ready for testing

### Phase 0.5: Warmup (ExecuteWarmupPhaseAsync)

1. **Warmup Events** - Publish and consume 200 events (30s timeout)
2. **Warmup API** - Run 5s API load test with 1 worker
3. **Re-clear Infrastructure** - Database and RabbitMQ cleared again

**Purpose:** JIT compilation, connection pool initialization, DNS resolution
**Error Handling:** Failures logged as warnings but don't abort test

### Phase 1: Event Throughput Test (ExecuteEventTestPhaseAsync)

**CRITICAL:** Publisher and consumer run **CONCURRENTLY** (not sequentially!)

```csharp
var consumerTask = _eventConsumer.StartTrackingEventsAsync(...);
var publisherTask = _eventPublisher.PublishEventsAsync(...);
await Task.WhenAll(consumerTask, publisherTask);
```

**Metrics Collected:**
- Publish throughput (events/second)
- Consume throughput (events/second)
- Total event processing time
- Throughput samples (100ms intervals)

### Phase 2: API Load Test (ExecuteApiTestPhaseAsync)

Delegates to k6-based IApiLoadTester:

```csharp
var result = await _apiLoadTester.StartTestAsync(
    config.ApiUrl,
    config.ApiDurationOrDefault,
    config.ApiWorkers,
    cancellationToken);
```

**Metrics Collected:**
- Total requests
- Request throughput (requests/second)
- P95/P99 response times
- Success/failure counts
- API throughput samples (100ms intervals)

### Phase 3: Reporting (ExecuteReportingPhaseAsync)

1. **Stop Monitoring** - IHost.StopAsync stops all BackgroundServices
2. **Collect Metrics** - Retrieve samples from all monitors
3. **Build TestReport** - Transform internal TestResult to TestReport
4. **Generate JSON Reports** - Create timestamped JSON files
5. **Generate Chart** - Create PNG visualization (5 subplots)

**Output Files:**
- `test-report-{timestamp}-{servicename}.json` - Main summary
- `test-report-{timestamp}-{servicename}.resource-metrics.json` - CPU/memory data
- `test-report-{timestamp}-{servicename}.events-throughput.json` - Event throughput
- `test-report-{timestamp}-{servicename}.api-throughput.json` - API throughput
- `test-report-{timestamp}-{servicename}.postgres-metrics.json` - PostgreSQL metrics
- `test-report-{timestamp}-{servicename}.rabbitmq-metrics.json` - RabbitMQ metrics
- `test-report-{timestamp}-{servicename}.chart.png` - Combined visualization

## Dependency Injection

```csharp
var builder = Host.CreateApplicationBuilder();

builder.Services.AddOrchestration(
    postgresConnectionString: "Host=localhost;Port=5432;...",
    rabbitMqConnectionString: "amqp://admin:admin@localhost:5672",
    rabbitMqContainerName: "performancetest-rabbitmq",    // Optional
    postgresContainerName: "performancetest-postgres");   // Optional

var host = builder.Build();

// Note: Do NOT call host.StartAsync() - orchestrator manages IHost lifecycle
// Use TestOrchestrator.BuildDependencies() + TestOrchestrator.RunTestAsync()
var deps = TestOrchestrator.BuildDependencies(host.Services, config, logger, ct);
var result = await TestOrchestrator.RunTestAsync(deps, config, progress, logger);
```

### Registered Services

`AddOrchestration()` registers:

**Phase 1: Infrastructure**
- `IServiceDiscovery` - Process discovery (Linux/Windows)
- `IDatabase` - PostgreSQL management
- `IRabbitMQ` - Queue purging

**Phase 2: Data Collection**
- `IEventPublisher` - CloudEvents publishing
- `IEventConsumer` - Event consumption with tracking
- `IMetricsCollector` - Throughput sample collection
- `WarmupDockerMonitors`, `StartDockerMonitoring`, `GetDockerMetrics` delegates - Container monitoring
- `IApiLoadTester` - k6-based API load testing

**Phase 3: Reporting**
- `ISystemInfoDetector` - Hardware/OS detection (cached)
- `IReportGenerator` - JSON report generation
- `IChartGenerator` - PNG chart generation

**Note:** `IProcessMonitor` is NOT registered by `AddOrchestration()` because it requires a process ID that's only discovered at runtime. `TestOrchestrator.BuildDependencies()` handles process monitoring registration separately after service discovery. The orchestrator itself (`TestOrchestrator`) is a static class -- not registered in DI -- and receives pre-composed delegates via `TestOrchestrator.Dependencies`.

## Usage Example

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PerformanceTester.Orchestration;

var builder = Host.CreateApplicationBuilder();

// Register all orchestration services
builder.Services.AddOrchestration(
    postgresConnectionString: "Host=localhost;Port=5432;Database=postgres;Username=admin;Password=admin",
    rabbitMqConnectionString: "amqp://admin:admin@localhost:5672");

var host = builder.Build();

// Configure test
var config = new TestConfiguration(
    EventCount: 10000,
    ApiDuration: TimeSpan.FromSeconds(30),
    ApiWorkers: 1,
    ServicePort: 8094,
    DatabaseName: "go_db",
    ResultsFolder: "./test-results");

// Run test
var deps = TestOrchestrator.BuildDependencies(host.Services, config, logger, cancellationToken);
var result = await TestOrchestrator.RunTestAsync(deps, config, progress: null, logger);

result.Match(
    success: s => Console.WriteLine($"Test complete: {s.Value.Results.Phase2Consume.ThroughputEventsPerSec:F2} events/s"),
    failure: f => Console.WriteLine($"Test failed in {f.Error.Phase}: {f.Error.Message}"));
```

## Design Decisions

### Concurrent Event Testing

**Critical:** Publisher and consumer run CONCURRENTLY to simulate real-world backpressure.

**Why?**
- More realistic (consumers don't wait for publisher to finish)
- Measures true end-to-end latency
- Tests service under load (queue filling up)
- Matches Python implementation behavior

**Anti-Pattern (DON'T):**
```csharp
// ❌ WRONG: Sequential execution
await _eventPublisher.PublishEventsAsync(count);  // Publish all first
await _eventConsumer.StartTrackingEventsAsync(count);  // Then consume
```

**Correct Pattern (DO):**
```csharp
// ✅ RIGHT: Concurrent execution
var consumerTask = _eventConsumer.StartTrackingEventsAsync(count);
var publisherTask = _eventPublisher.PublishEventsAsync(count);
await Task.WhenAll(consumerTask, publisherTask);
```

### Warmup Phase

**Purpose:** Eliminate JIT compilation and cold-start effects from measurements.

**What Gets Warmed Up:**
- .NET JIT compilation (first method invocations)
- Database connection pools (initial connections)
- RabbitMQ connection pools (channel creation)
- DNS resolution (hostname lookups cached)
- OS file system cache (binaries loaded into memory)

**Failure Handling:** Warmup failures don't abort test (logged as warnings).

**Why?**
- Warmup issues (timeouts, connection failures) shouldn't prevent measurement
- Test can proceed even if warmup partially succeeds
- Failures indicate potential issues but aren't test blockers

### IHost Lifecycle Management

Orchestrator manages IHost lifecycle internally:

```csharp
// Setup Phase
await _host.StartAsync();  // Starts all BackgroundServices

// Test runs...

// Reporting Phase
await _host.StopAsync();  // Stops all BackgroundServices
```

**Why?**
- Precise control over monitoring windows
- Ensures clean startup/shutdown
- Prevents metric collection gaps
- Matches test phase boundaries

### Keyed Services for Docker Monitors

Uses .NET 8+ keyed services for container monitoring:

```csharp
var getDockerMetrics = services.GetRequiredService<GetDockerMetrics>();
// getDockerMetrics(config.RabbitMqContainerName)
// getDockerMetrics(config.PostgresContainerName)
```

**Why?**
- Same interface, different instances
- Type-safe dependency resolution
- Clear intent in constructor
- Better than named/qualified dependencies

### TestResult → TestReport Transformation

Internal `TestResult` aggregates raw data, then transforms to `TestReport`:

**TestResult** (internal):
- Uses slice-owned models (PublishMetrics, ApiLoadTestResult, etc.)
- Contains raw samples (EventThroughputSample, ProcessMetrics, etc.)
- Minimal transformation, just aggregation

**TestReport** (public):
- Uses reporting models (ProcessResourceSample, ThroughputMetricSample, etc.)
- Adds calculated fields (ElapsedSeconds from test start)
- Matches Python output format exactly

**Why?**
- Separation of concerns (orchestration vs reporting)
- Reporting owns output format
- Easy to add new output formats (CSV, HTML, etc.)

## Error Handling

### Setup Phase Errors

**Service Not Found:**
```csharp
throw new TimeoutException(
    $"Service not found on port {port} within 30 seconds. " +
    "Ensure the service is running and listening on the specified port.");
```

**Database/RabbitMQ Failures:**
- Wrapped in InvalidOperationException
- Include retry details from underlying services
- Abort test (can't proceed without clean infrastructure)

### Warmup Phase Errors

**Any Failure:**
- Logged as warning
- Test continues
- Potential impact on initial measurements

**Example Log:**
```
[WARN] Warmup phase failed, continuing with measured test.
       This may affect initial JIT compilation times.
```

### Event Test Phase Errors

**Consumer Timeout:**
```csharp
throw new TimeoutException(
    $"Inactivity timeout expired. Received {current}/{expected} events. " +
    "No events received for {timeout}s.");
```

**Publisher Failures:**
- Wrapped in InvalidOperationException
- Include RabbitMQ connection details
- Abort test (can't measure throughput without events)

### API Test Phase Errors

**k6 Failures:**
- Wrapped in InvalidOperationException
- Include k6 exit code and stderr
- Abort test (can't measure API performance)

### Reporting Phase Errors

**Monitor Collection Failures:**
- Return empty collections
- Logged as warnings
- Report generated with available data

**File Write Failures:**
- Propagate to caller
- Include path and permission details
- Ensure directory creation before write

## Integration with Other Slices

### Phase 1: Infrastructure (Setup)

```csharp
// Service discovery
var processId = await _serviceDiscovery.FindServiceProcessIdAsync(port, timeout);

// Database cleanup
await _database.ClearDatabaseAsync(databaseName);

// RabbitMQ cleanup
await _rabbitMq.ClearAllQueuesAsync();
```

### Phase 2: Event Publishing

```csharp
// Publish events
var metrics = await _eventPublisher.PublishEventsAsync(count);
// Returns: PublishMetrics (event count, duration, throughput)
```

### Phase 2: Event Consuming

```csharp
// Start consumer tracking (returns when done or timeout)
await _eventConsumer.StartTrackingEventsAsync(expectedCount, timeout);

// Collect throughput samples after test
var samples = _metricsCollector.GetThroughputSamples();
// Returns: IReadOnlyCollection<EventThroughputSample>
```

### Phase 2: Process Monitoring

**Note:** Not registered by AddOrchestration (requires runtime process ID).

```csharp
// After service discovery, register process monitor
builder.Services.AddProcessMonitoring(processId, samplingInterval);

// Collect metrics after test
var metrics = _processMonitor.GetCollectedMetrics();
// Returns: IReadOnlyCollection<ProcessMetrics>
```

### Phase 2: Docker Monitoring

```csharp
// Registered by AddOrchestration with keyed services
var getDockerMetrics = services.GetRequiredService<GetDockerMetrics>()

// Collect metrics after test
var metrics = rabbitMqMonitor.GetCollectedMetrics();
// Returns: IReadOnlyCollection<DockerMetrics>
```

### Phase 2: API Load Testing

```csharp
// Start k6 test
var result = await _apiLoadTester.StartTestAsync(url, duration, workers);
// Returns: ApiLoadTestResult (requests, throughput, P95/P99, samples)
```

### Phase 3: Reporting

```csharp
// Generate JSON reports
await _reportGenerator.GenerateReportAsync(outputDir, testReport);

// Generate PNG chart
await _chartGenerator.GenerateChartAsync(chartPath, testReport);
```

## Troubleshooting

### "Service not found on port X"

**Cause:** Service not running or not listening on expected port.

**Fix:**
1. Verify service is running: `ps aux | grep serviceName`
2. Check port binding: `lsof -i :8094` (Linux) or `netstat -ano | findstr :8094` (Windows)
3. Ensure service started successfully (check logs)

### "Inactivity timeout expired"

**Cause:** Consumer not receiving events (0/N received).

**Fix:**
1. Check RabbitMQ connection (service logs)
2. Verify queue bindings (RabbitMQ management UI)
3. Check for consumer errors (service logs)
4. Increase timeout if service is slow

### "k6 binary not found"

**Cause:** k6 not installed or not in PATH.

**Fix:**
```bash
# Install k6
sudo snap install k6  # Linux
brew install k6       # macOS
choco install k6      # Windows
```

### "Docker monitor failed to collect metrics"

**Cause:** Docker daemon not accessible or container not found.

**Fix:**
1. Verify Docker is running: `docker ps`
2. Check container exists: `docker ps -a | grep containername`
3. Ensure Docker socket accessible (Linux: `/var/run/docker.sock`)

### "Report generation failed - disk full"

**Cause:** Insufficient disk space for JSON/PNG outputs.

**Fix:**
1. Check disk space: `df -h` (Linux) or `Get-PSDrive` (Windows)
2. Clean old test results: `rm -rf ./test-results/*`
3. Use different output directory with more space

## Performance Characteristics

**Typical Test Duration (10K events, 30s API):**
- Setup: 2-5 seconds
- Warmup: 3-8 seconds
- Event throughput: 5-60 seconds (depends on service)
- API load test: 30 seconds (configured)
- Reporting: 2-5 seconds
- **Total:** ~45-110 seconds

**Memory Usage:**
- Orchestrator: ~50 MB
- Monitoring services: ~20 MB per monitor
- k6: ~100-200 MB (depends on VU count)
- **Peak:** ~250-400 MB for full test

**Disk Usage (per test):**
- JSON reports: ~500 KB - 2 MB
- PNG chart: ~200-500 KB
- **Total:** ~1-3 MB per test

## Vertical Slice Architecture

This slice demonstrates **excellent VSA principles**:

### ✅ Producer-Owned Contracts

Orchestration consumes interfaces from other slices:
- Infrastructure: `IServiceDiscovery`, `IDatabase`, `IRabbitMQ`
- EventPublishing: `IEventPublisher`, `PublishMetrics`
- EventConsuming: `IEventConsumer`, `IMetricsCollector`, `EventThroughputSample`
- ProcessMonitoring: `IProcessMonitor`, `ProcessMetrics`
- DockerMonitoring: `WarmupDockerMonitors`, `StartDockerMonitoring`, `GetDockerMetrics`, `DockerMetrics`
- ApiLoadTesting: `IApiLoadTester`, `ApiLoadTestResult`, `ApiThroughputSample`
- Reporting: `IReportGenerator`, `IChartGenerator`, `TestReport`

**No coupling through shared models** - each slice owns its contracts.

### ✅ Self-Contained Registration

Single method registers entire slice:

```csharp
builder.Services.AddOrchestration(
    postgresConnectionString: "...",
    rabbitMqConnectionString: "...");
```

Internally delegates to other slices:
```csharp
services.AddInfrastructure(...);
services.AddEventPublishing(...);
services.AddEventConsuming(...);
services.AddDockerMonitoring(...);
services.AddApiLoadTesting();
services.AddReporting();
```

### ✅ Clear Ownership

**Orchestration owns:**
- Test workflow coordination
- Phase execution order
- TestConfiguration model
- TestResult internal model (bridge to reporting)

**Orchestration does NOT own:**
- Monitoring implementations (ProcessMonitoring, DockerMonitoring)
- Publishing/consuming logic (EventPublishing, EventConsuming)
- Report generation (Reporting)

### ✅ Minimal Dependencies

Only depends on:
- Phase 1-3 production slices (via interfaces)
- No test project dependencies
- No coupling to other orchestrators

**Perfect vertical slice** - focused, independent, composable.

---

**Status:** ✅ Complete (Phase 4)
**Dependencies:** Infrastructure, EventPublishing, EventConsuming, ProcessMonitoring, DockerMonitoring, ApiLoadTesting, Reporting
**Test Coverage:** See Phase 4 integration tests (planned)
