# PerformanceTester.Orchestration Project - Comprehensive Exploration

## Project Overview

The Orchestration module is the **heart** of the performance testing framework. It orchestrates a complete multi-phase performance test workflow from service discovery through final report generation.

### Directory Structure
- `/src/PerformanceTester.Orchestration/` - Core orchestration library
  - `TestOrchestrator.cs` - Main orchestrator (implementation)
  - `ITestOrchestrator.cs` - Public interface
  - `TestConfiguration.cs` - Configuration record with defaults
  - `TestResult.cs` - Internal result aggregation
  - `ServiceCollectionExtensions.cs` - DI registration

- `/src/PerformanceTester.Orchestration.IntegrationTests/` - Comprehensive test suite
  - `OrchestratorCompleteWorkflowTests.cs` - Happy path (Phase 0-4)
  - `OrchestratorErrorHandlingTests.cs` - Error scenarios
  - `OrchestratorCancellationTests.cs` - Cancellation behavior
  - `Infrastructure/` - Test infrastructure and helpers

---

## TestOrchestrator Responsibilities and Architecture

### Core Responsibilities

1. **Service Discovery** (Phase 0 Setup)
   - Discovers service by port using `IServiceDiscovery.FindServiceProcessIdAsync()`
   - Timeout: 30 seconds
   - Returns process ID for monitoring

2. **Infrastructure Setup** (Phase 0 Setup)
   - Starts `IHost` (all BackgroundServices start here)
   - Starts process monitoring via `IProcessMonitor.StartMonitoring()`
   - Clears PostgreSQL database via `IDatabase.ClearDatabaseAsync()`
   - Clears RabbitMQ queues via `IRabbitMQ.ClearAllQueuesAsync()`
   - Connects to RabbitMQ publisher via `IEventPublisher.ConnectAsync()`

3. **Warmup Phase** (Phase 0.5)
   - Publishes warmup events (default: 200 events)
   - Consumes warmup events
   - Makes warmup HTTP calls to API (default: 10 calls)
   - Clears database/queues again before measured test
   - **Failures abort the test** (not logged-only)

4. **Event Throughput Test** (Phase 1 & 2)
   - **Concurrent execution**: Publisher and Consumer start simultaneously
   - Phase 1 (Publish): Events published as fast as possible
   - Phase 2 (Consume): Events consumed as received
   - Measures throughput (events/second)
   - Collects throughput samples via `IMetricsCollector`

5. **API Load Test** (Phase 3)
   - Uses `IApiLoadTester.StartTestAsync()` (k6-based)
   - Configured duration (default: 30 seconds)
   - Configured concurrency (default: 1 worker)
   - Measures request throughput and response times (P95, P99)
   - Stops on max consecutive failures (default: 3)

6. **Metrics Collection & Reporting** (Phase 3 Cleanup & Phase 4)
   - Stops all monitoring via `IHost.StopAsync()`
   - Collects metrics from all monitors:
     - `IMetricsCollector.GetThroughputSamples()`
     - `IProcessMonitor.GetCollectedMetrics()` (CPU, memory, threads)
     - `IDockerMonitor.GetCollectedMetrics()` for RabbitMQ and PostgreSQL
   - Gets system info via `ISystemInfoDetector.GetSystemInfoAsync()`
   - Generates JSON reports via `IReportGenerator.GenerateReportAsync()`
   - Generates PNG chart via `IChartGenerator.GenerateChartAsync()`

### Dependency Tree

```
TestOrchestrator depends on:
├── Phase 1-3 Infrastructure
│   ├── IServiceDiscovery (find service by port)
│   ├── IDatabase (clear test data)
│   └── IRabbitMQ (clear queues)
├── Phase 1: Event Publishing
│   ├── IEventPublisher (publish to RabbitMQ)
├── Phase 2: Event Consuming
│   ├── IEventConsumer (consume from RabbitMQ)
│   └── IMetricsCollector (collect throughput samples)
├── Phase 3: API Load Testing
│   ├── IApiLoadTester (k6-based load testing)
├── Continuous Monitoring (all phases)
│   ├── IProcessMonitor (service CPU/memory)
│   ├── IEnumerable<IDockerMonitor> (RabbitMQ, PostgreSQL)
├── Phase 4: Reporting
│   ├── ISystemInfoDetector (OS, CPU, RAM info)
│   ├── IReportGenerator (JSON reports)
│   └── IChartGenerator (PNG visualization)
└── IHost (manages BackgroundServices lifecycle)
```

### Key Design Patterns

1. **Phased Execution**
   - Phase 0: Setup (infrastructure)
   - Phase 0.5: Warmup (discarded data)
   - Phase 1: Publish events
   - Phase 2: Consume events (concurrent with Phase 1)
   - Phase 3: API load test
   - Phase 4: Reporting and cleanup

2. **Concurrent Publisher/Consumer**
   - Both start simultaneously via `Task.WhenAll()`
   - Tests real concurrent throughput, not sequential
   - Consumer waits for all events with inactivity timeout

3. **Graceful Error Handling**
   - Setup phase failures: throw immediately
   - Warmup failures: throw immediately (aborts test)
   - Event/API phase failures: attempt cleanup then throw
   - Always attempts to stop monitoring services on error

4. **Resource Cleanup**
   - Database cleared between warmup and measured test
   - RabbitMQ queues cleared between warmup and measured test
   - 500ms delay after queue purge (allows consumers to recover)
   - Monitoring services stopped before metric collection

---

## Public API

### `ITestOrchestrator`
```csharp
public interface ITestOrchestrator
{
    Task<TestReport> RunTestAsync(
        TestConfiguration configuration,
        CancellationToken cancellationToken = default);
}
```

### `TestConfiguration` Record
- **EventCount** (default: 10000) - Events to publish/consume
- **ApiDuration** (default: 30s) - API load test duration
- **ApiWorkers** (default: 1) - Concurrent API workers
- **InactivityTimeout** (default: 120s) - Consumer timeout
- **WarmupEventCount** (default: 200) - Warmup events
- **WarmupApiCallCount** (default: 10) - Warmup HTTP calls
- **WarmupInactivityTimeout** (default: 30s) - Warmup consumer timeout
- **ServicePort** (default: 8080) - Service port
- **DatabaseName** (required) - PostgreSQL database
- **ResultsFolder** (default: ./test-results) - Output directory
- **RabbitMqContainerName** (default: performancetest-rabbitmq)
- **PostgresContainerName** (default: performancetest-postgres)
- **MaxConsecutiveApiFailures** (default: 3)

Helper properties:
- `ApiUrl` - Computed from ServicePort: `http://localhost:{ServicePort}/kpi`
- `ApiDurationOrDefault`, `InactivityTimeoutOrDefault`, etc.

---

## Current Test Coverage

### Existing Tests

1. **OrchestratorCompleteWorkflowTests.cs** - Happy path
   - ✓ Complete workflow with all phases
   - ✓ Service discovery verification
   - ✓ Warmup phase execution
   - ✓ Phase 1 (Publish) metrics
   - ✓ Phase 2 (Consume) metrics
   - ✓ Concurrent publish/consume verification
   - ✓ Phase 3 (API Load) metrics
   - ✓ Resource monitoring (Process, RabbitMQ, PostgreSQL)
   - ✓ Report generation (8+ files)
   - ✓ System info collection

2. **OrchestratorErrorHandlingTests.cs** - Error scenarios
   - ✓ Service not running → TimeoutException (setup phase)
   - ✓ Consumer timeout (insufficient events) → TimeoutException with progress (1/2)

3. **OrchestratorCancellationTests.cs** - Cancellation behavior
   - ✓ Cancellation during event phase → OperationCanceledException
   - ✓ Cancellation during API phase → OperationCanceledException

### Gaps in Current Coverage

**Phase-specific scenarios:**
- [ ] Database clear failures
- [ ] RabbitMQ queue clear failures
- [ ] Service discovery timeout edge cases
- [ ] Warmup phase failure scenarios
- [ ] API load test timeout/failure handling
- [ ] Report generation failures

**Resource monitoring:**
- [ ] Process monitor start failures
- [ ] Docker monitor failures
- [ ] Metrics collection edge cases
- [ ] System info detection failures

**Concurrency & timing:**
- [ ] Race conditions in concurrent publish/consume
- [ ] Timing-sensitive scenarios (fast completion)
- [ ] Late consumer start (after publisher completes)
- [ ] Long-running tests with many samples

**Configuration validation:**
- [ ] Invalid configuration values
- [ ] Port already in use
- [ ] Results folder creation failures
- [ ] Database name validation

**Cleanup & error recovery:**
- [ ] Partial cleanup on failure
- [ ] Hung background services
- [ ] RabbitMQ connection loss during test
- [ ] PostgreSQL connection loss during test

---

## Test Infrastructure & Helpers

### Base Classes

1. **IntegrationTest**
   - Extends `IntegrationTestBase<OrchestrationSystem>`
   - Provides `System` property (OrchestrationSystem instance)
   - Provides `Output` for test output logging

### OrchestrationSystem (System Under Test)

**Key Components:**
- `Orchestration` - ITestOrchestrator facade
- `DotNetAotService` - .NET 9 AOT service manager (starts/stops service)
- `ConfigurableReferenceService` - Bi-directional mock service for testing
- Inherited from base System:
  - `PostgreSQL` - PostgreSQL test container
  - `RabbitMQ` - RabbitMQ test container

**Lifecycle Methods:**
- `InitializeSystemAsync()` - Creates databases, services, connections
- `WaitForServiceHealthyAsync()` - Polls /health endpoint until ready
- `Dispose()` - Cleanup services and containers

**Integration Test Database:**
- Constant: `IntegrationTestDatabaseName` = `"dotnet9aot_perftest_integrationtest_db"`
- Isolated from production database
- Persists between test runs (data cleared, not DB dropped)

### TestConfigurationBuilder

**Fluent builder** for `TestConfiguration`:
```csharp
var config = new TestConfigurationBuilder()
    .WithEventCount(500)
    .WithApiDuration(TimeSpan.FromSeconds(10))
    .WithDatabaseName("test_db")
    .WithResultsFolder("./results")
    .Build();
```

**Available methods:** `WithEventCount()`, `WithApiDuration()`, `WithApiWorkers()`, `WithServicePort()`, `WithDatabaseName()`, `WithResultsFolder()`, `WithRabbitMqContainerName()`, `WithPostgresContainerName()`, `WithInactivityTimeout()`, `WithWarmupEventCount()`, `WithWarmupApiCallCount()`, `WithWarmupInactivityTimeout()`

### Custom Assertions

**OrchestrationAssertions** (via JoanComasFdz.AssertingThat library):
- `CompletedAllPhases(report)` - Verifies all phases ran successfully
- `ThrowsTimeoutExceptionWhenServiceNotFound(config)` - Service discovery timeout
- `ThrowsConsumerInactivityTimeout(config, expectedReceivedCount)` - Consumer timeout with progress

Usage:
```csharp
await Asserting.That(System.Orchestration.Orchestrator)
    .CompletedAllPhases(report);
```

### Infrastructure Helpers

**Orchestration.cs** - ITestOrchestrator facade
- Creates IHost with all services via `AddOrchestration()`
- `StartHostAsync()` - Start IHost manually (for direct consumer testing)
- `GetEventConsumer()` - Access IEventConsumer directly
- Disposes gracefully (StopAsync before Dispose)

**DotNetAotServiceManager.cs** - Service lifecycle
- `StartAsync(databaseName)` - Build and start .NET 9 AOT service
- `Stop()` - Stop running service
- Configures environment variables for database/RabbitMQ connections

**ConfigurableReferenceService.cs** - Mock service for testing
- Bi-directional: consumes input events, publishes output events
- `ConfigurePublication(eventCount, warmupEventCount)` - Set how many events to publish
- `ConnectAndSubscribeAsync(listenPort)` - Start consuming/publishing
- `DisconnectAsync()` - Stop and cleanup
- Used to test consumer timeout scenarios

---

## Key Testing Patterns

### Pattern 1: Happy Path Test
```csharp
await System.DotNetAotService.StartAsync(databaseName);
var config = new TestConfigurationBuilder()
    .WithEventCount(500)
    .Build();

var report = await System.Orchestration.Orchestrator.RunTestAsync(config);

Assert.True(report.Results.Phase1Publish.DurationSeconds > 0);
Assert.NotEmpty(report.ProcessResourceSamples);
```

### Pattern 2: Error Handling Test
```csharp
System.DotNetAotService.Stop();
var config = /* ... */;

var exception = await Assert.ThrowsAsync<TimeoutException>(
    () => System.Orchestration.Orchestrator.RunTestAsync(config));

Assert.Contains("Service not found", exception.Message);
```

### Pattern 3: Service Configuration Test
```csharp
System.ConfigurableReferenceService.ConfigurePublication(
    eventCount: 1,  // Will timeout - expects 2
    warmupEventCount: 1);

await System.ConfigurableReferenceService
    .ConnectAndSubscribeAsync(listenPort: 9999);

await System.WaitForServiceHealthyAsync(port: 9999);

var exception = await Assert.ThrowsAsync<TimeoutException>(
    () => System.Orchestration.Orchestrator.RunTestAsync(config));
```

### Pattern 4: Cleanup After Test
```csharp
try
{
    // Test code
}
finally
{
    // CRITICAL: Purge ConfigurableReferenceService queue
    await System.RabbitMQ.PurgeQueueAsync(
        ConfigurableReferenceService.DefaultInputQueueName);
}
```

---

## Critical Implementation Details

### Concurrent Publish/Consume
```csharp
// CRITICAL: Publisher and consumer MUST start concurrently!
var consumerTask = _eventConsumer.StartTrackingEventsAsync(count, timeout, ct);
var publisherTask = _eventPublisher.PublishEventsAsync(count, ct);
await Task.WhenAll(consumerTask, publisherTask);
```

### Queue Purge Recovery
```csharp
// RabbitMQ consumers need time to recover after queue purge
await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
```

### Error Cleanup
```csharp
catch (Exception ex)
{
    // Try to disconnect publisher (can hang - use CancellationToken.None)
    try { await _eventPublisher.DisconnectAsync(CancellationToken.None); }
    catch { /* Log but continue */ }
    
    // Stop host (can hang - use CancellationToken.None)
    try { await _host.StopAsync(CancellationToken.None); }
    catch { /* Log but continue */ }
    
    throw; // Re-throw original exception
}
```

### Metrics Calculation
```csharp
// Event throughput = event count / actual test duration (not just publish time)
var eventTestDuration = eventTestEndTime - eventTestStartTime;
var eventThroughput = config.EventCount / eventTestDuration.TotalSeconds;

// Publish throughput comes from PublishMetrics directly
var publishThroughput = publishMetrics.EventsPerSecond;
```

---

## Test Execution Sequence

1. **[INIT]** `OrchestrationSystem.InitializeSystemAsync()`
   - Close all RabbitMQ connections
   - Create integration test database
   - Create DotNetAotServiceManager
   - Create Orchestration (IHost)
   - Create ConfigurableReferenceService (fresh instance)

2. **[TEST]** Test method executes
   - Start service(s) as needed
   - Configure settings
   - Run orchestrator
   - Assert results

3. **[CLEANUP]** Finally block
   - Purge ConfigurableReferenceService queue
   - (System.Dispose() called by test framework)

4. **[DISPOSE]** `OrchestrationSystem.Dispose()`
   - Stop DotNetAotService
   - Dispose ConfigurableReferenceService
   - Dispose Orchestration (stops IHost)
   - Dispose base (containers)

---

## Important Notes for Test Development

1. **Test Isolation**: Tests must clean up shared RabbitMQ state (ConfigurableReferenceService queue) in finally block
2. **Service Lifecycle**: Always start service before running orchestrator
3. **Warmup Importance**: Warmup tests service connectivity and health before measured test
4. **Concurrent Execution**: Event test must run publisher and consumer concurrently, not sequentially
5. **Metrics Precision**: Throughput calculated from actual elapsed time, not component timings
6. **Error Messages**: Include helpful context (port, timeout, progress) in exceptions
7. **Configuration Flexibility**: TestConfigurationBuilder allows tests to override only needed values
8. **Infrastructure Availability**: PostgreSQL and RabbitMQ must be running (provided by testcontainers)
