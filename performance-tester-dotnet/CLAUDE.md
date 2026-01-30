# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in the performance-tester-dotnet repository.

## Project Overview

This is a **specialized .NET 9 subsystem** of the larger cross-language performance comparison project. It provides shared infrastructure and testing utilities written in idiomatic .NET 9, following **Vertical Slice Architecture (VSA)** principles.

**Purpose:** Reusable .NET libraries for:
- Integration testing infrastructure (Testcontainers lifecycle management)
- Service discovery and database management utilities
- CloudEvents-compliant event publishing to RabbitMQ
- Performance test orchestration (future phases)

**Relationship to Parent Project:** The parent `/workspace/` directory contains 11 microservice implementations across different languages (Bun, Go, Java, Python, Rust, .NET, etc.). This subsystem provides the .NET-specific testing and infrastructure tooling.

## Quick Start

### Prerequisites

- .NET 9.0 SDK
- Docker (must be running)
- Docker Compose (for infrastructure)

### Build All Projects

```bash
cd /workspace/performance-tester-dotnet
dotnet build
```

### Run All Tests

```bash
# Run full test suite (takes ~1-2 minutes)
dotnet test

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific test project
dotnet test src/PerformanceTester.IntegrationTesting.Tests
```

### Performance Expectations

- **First test execution:** ~20-25 seconds (Testcontainers startup + health checks)
- **Subsequent tests:** ~100ms overhead per test (DI container creation)
- **Full Phase 0+1+2 suite:** ~1-2 minutes

## Architecture

### Vertical Slice Architecture (VSA)

This project follows **Vertical Slice Architecture** rather than traditional layered architecture:

- ✅ Each slice owns its models (no central "Core" project)
- ✅ Producers expose interfaces that consumers depend on
- ✅ Each slice has its own DI registration method (`AddXxx()`)
- ✅ Maximum independence between slices
- ✅ Each slice can be developed and tested in isolation

**Why VSA?**
- Self-contained capabilities (each slice does one thing well)
- Producer-owned contracts (no shared model layer)
- Better testability (isolated integration tests per slice)
- Clear ownership boundaries (no coupling through shared layer)

### Solution Structure

```
PerformanceTester.sln (14 projects)
│
├── Foundation Layer
│   ├── PerformanceTester.Common/                 # Cross-platform utilities (OS detection)
│   ├── PerformanceTester.IntegrationTesting/     # Container lifecycle management
│   └── PerformanceTester.IntegrationTesting.Tests/
│
├── Phase 1: Infrastructure (COMPLETE ✅)
│   ├── PerformanceTester.Infrastructure/         # Service discovery, DB, RabbitMQ mgmt
│   └── PerformanceTester.Infrastructure.IntegrationTests/
│
├── Phase 2: Data Collection Slices (COMPLETE ✅)
│   ├── PerformanceTester.EventPublishing/        # CloudEvents to RabbitMQ
│   ├── PerformanceTester.EventPublishing.IntegrationTests/
│   ├── PerformanceTester.EventConsuming/         # RabbitMQ consumer with inactivity timeout
│   ├── PerformanceTester.EventConsuming.IntegrationTests/
│   ├── PerformanceTester.ProcessMonitoring/      # CPU, memory, threads monitoring
│   ├── PerformanceTester.ProcessMonitoring.IntegrationTests/
│   ├── PerformanceTester.DockerMonitoring/       # Docker container stats
│   ├── PerformanceTester.DockerMonitoring.IntegrationTests/
│   ├── PerformanceTester.ApiLoadTesting/         # k6 integration
│   └── PerformanceTester.ApiLoadTesting.IntegrationTests/
│
├── Phase 3: Reporting (COMPLETE ✅)
│   ├── PerformanceTester.Reporting/              # JSON reports, PNG charts, comparisons
│   └── PerformanceTester.Reporting.IntegrationTests/
│
└── Phase 4: Orchestration (COMPLETE ✅)
    ├── PerformanceTester.Orchestration/          # Test workflow coordination
    └── PerformanceTester.Orchestration.IntegrationTests/
```

### Project Dependencies

```
Phase 0: IntegrationTesting + Common (foundation)
    ↓
Phase 1: Infrastructure (service discovery, DB, RabbitMQ utilities)
    ↓
Phase 2: [6 Independent Slices - All COMPLETE]
    ├─ EventPublishing (owns CloudEvent)
    ├─ EventConsuming (owns ThroughputSample)
    ├─ ProcessMonitoring (owns ProcessMetrics)
    ├─ DockerMonitoring (owns DockerMetrics)
    └─ ApiLoadTesting (owns K6Result)
    ↓
Phase 3: Reporting (aggregates data from all slices)
    ↓
Phase 4: Orchestration (coordinates all slices)
    ↓
Phase 5: CLI (NOT STARTED - entry point)
```

**Key Insight:** All production projects are independent (no project-to-project dependencies). Only test projects depend on `IntegrationTesting` for shared infrastructure.

## Project Phases Status

### ✅ Phase 0: Integration Testing Foundation (COMPLETE)

**Deliverables:**
- `ContainerManager` - Singleton managing PostgreSQL + RabbitMQ Testcontainers
- `IntegrationTestBase` - Abstract base class for all integration tests
- xUnit logging integration - Captures structured logs in test output
- 8 verification tests (all passing)

**Test Coverage:**
1. ✅ PostgreSQL container starts successfully
2. ✅ RabbitMQ container starts successfully
3. ✅ ContainerManager is singleton across multiple calls
4. ✅ EnsureStartedAsync is idempotent
5. ✅ XunitLogger writes to test output
6. ✅ LoggingTestExtensions adds xUnit output to logging builder
7. ✅ IntegrationTestBase initializes containers
8. ✅ ContainerManager health checks prevent connection failures

**Key Design Decisions:**
- Containers start once per test session (singleton pattern)
- Health checks poll until ready (30s timeout)
- Containers never stopped (performance optimization)
- Each test gets fresh DI container (test isolation)

### ✅ Phase 1: Infrastructure Slice (COMPLETE)

**Deliverables:**
- `IServiceDiscovery` - Cross-platform process discovery (Linux/Windows)
- `IDatabase` - Dynamic table truncation with CASCADE
- `IRabbitMQ` - Queue discovery and purging
- 15 integration tests (all passing)

**Key Features:**
- **Service Discovery:** Finds PID listening on port (uses `lsof` on Linux, PowerShell on Windows)
- **Database Management:** Dynamic table discovery (no hardcoded names), retry logic
- **RabbitMQ Management:** Queue purging with connection pooling
- **Strategy Pattern:** Platform detection at DI registration (not per method call)

**DI Registration:**
```csharp
builder.Services.AddInfrastructure(
    postgresConnectionString: "Host=localhost;...",
    rabbitMqConnectionString: "amqp://admin:admin@localhost:5672"
);
```

### ✅ Phase 2: Event Publishing Slice (COMPLETE)

**Deliverables:**
- `IEventPublisher` - CloudEvents publishing to RabbitMQ
- CloudEvents v1.0 compliance via official `CloudNative.CloudEvents` library
- Connection pooling (single IConnection, per-operation IChannel)
- Polly retry policy with exponential backoff
- Throughput tracking (events/second calculation)
- 6 integration tests (all passing)

**Key Features:**
- Random device ID generation (DEVICE-001 to DEVICE-999)
- Realistic status transitions (IDLE, RUNNING, ERROR)
- Exchange: "referenceservice.comparison" (topic, durable)
- Routing key: "instrument.status.changed"
- Persistent delivery mode

**DI Registration:**
```csharp
builder.Services.AddEventPublishing(
    rabbitMqConnectionString: "amqp://admin:admin@localhost:5672"
);
```

**Usage:**
```csharp
var publisher = host.Services.GetRequiredService<IEventPublisher>();
var metrics = await publisher.PublishEventsAsync(count: 1000);
// Prints: Published 1000 events in 2.34s (427.35 events/sec)
```

### ✅ Phase 2b: Event Consuming Slice (COMPLETE)

**Deliverables:**
- `IEventConsumer` - RabbitMQ consumption with inactivity timeout
- `ThroughputTracker` - 500ms interval throughput sampling
- `IMetricsCollector` - Thread-safe metrics collection via Channels
- BackgroundService lifecycle management
- Integration tests (all passing)

**Key Improvement over Python:**
- **Inactivity timeout** (120s since last event) vs Python's absolute timeout
- Allows slow-but-progressing services to complete
- Detects truly stuck services

### ✅ Phase 2c: Process Monitoring Slice (COMPLETE)

**Deliverables:**
- `IProcessMonitor` - Process resource monitoring interface
- `ProcessMonitorService` - BackgroundService with deferred start pattern
- `ProcessMetrics` - CPU%, memory MB, thread count snapshots
- 500ms sampling interval with PeriodicTimer
- Integration tests (all passing)

### ✅ Phase 2d: Docker Monitoring Slice (COMPLETE)

**Deliverables:**
- `IDockerMonitor` - Container monitoring interface
- `DockerMonitorService` - BackgroundService for container stats
- `DockerClientWrapper` - Docker API interaction via Docker.DotNet
- Multiple container concurrent monitoring
- Integration tests (all passing)

### ✅ Phase 2e: API Load Testing Slice (COMPLETE)

**Deliverables:**
- `IApiLoadTester` - k6 load test interface
- `ApiLoadTestService` - k6 script generation and execution
- `K6ScriptGenerator` - Customizable k6 scripts
- `K6MetricsParser` - Real-time JSON output parsing
- `MetricsAggregator` - Result aggregation (p95, p99, throughput)
- Integration tests (all passing)

### ✅ Phase 3: Reporting (COMPLETE)

**Deliverables:**
- `IReportGenerator` - 7 JSON file types per test run
- `IChartGenerator` - 5-subplot PNG visualization (ScottPlot)
- `IComparisonReportGenerator` - Markdown cross-service comparison
- `StatisticsCalculator` - Statistical utilities (std dev, CV%, percentiles)
- `SystemInfoDetector` - Hardware/OS detection
- 27 integration tests (all passing)

### ✅ Phase 4: Orchestration (COMPLETE)

**Deliverables:**
- `ITestOrchestrator` - Complete workflow coordination
- `TestOrchestrator` - 7-phase test execution engine
- `TestConfiguration` - Test parameters record
- `TestResult` - Raw test data aggregation
- Concurrent publish/consume (not sequential)
- 14+ integration tests (all passing)

**Workflow Phases:**
1. Setup → 2. Warmup → 3. Publish → 4. Consume → 5. API Load → 6. Reporting

### 📋 Phase 5: CLI (NOT STARTED)

**Remaining work:**
- `PerformanceTester.Cli` - Command-line interface entry point
- System.CommandLine for argument parsing
- Serilog for structured logging
- Host builder wiring all services together

## Common Development Tasks

### Adding a New Slice (Phase 3+)

1. **Create production project:**
   ```bash
   dotnet new classlib -n PerformanceTester.YourSlice
   dotnet sln add src/PerformanceTester.YourSlice
   ```

2. **Create integration test project:**
   ```bash
   dotnet new xunit -n PerformanceTester.YourSlice.IntegrationTests
   dotnet sln add src/PerformanceTester.YourSlice.IntegrationTests
   ```

3. **Add reference to IntegrationTesting (test project only):**
   ```bash
   cd src/PerformanceTester.YourSlice.IntegrationTests
   dotnet add reference ../PerformanceTester.IntegrationTesting
   ```

4. **Create slice-specific integration test base:**
   ```csharp
   public class IntegrationTest : IntegrationTestBase
   {
       protected System System { get; }

       public IntegrationTest(ITestOutputHelper output) : base(output)
       {
           System = new System(Output);
       }
   }
   ```

5. **Follow VSA principles:**
   - ✅ Own your models (no shared Core project)
   - ✅ Expose interface for consumers (`IYourSlice`)
   - ✅ Provide DI extension method (`AddYourSlice()`)
   - ✅ Keep slice independent (no project references to other slices)

### Test Project Dependencies (Anti-Pattern Warning)

**CRITICAL: Test projects should NEVER reference other test projects.**

**Anti-Pattern (DON'T DO THIS):**
```csharp
// ❌ BAD: EventConsuming.IntegrationTests referencing EventPublishing.IntegrationTests
<ProjectReference Include="..\PerformanceTester.EventPublishing.IntegrationTests\..." />

// In test code:
public EventPublishing.IntegrationTests.Infrastructure.EventPublishing EventPublishing { get; }
await System.EventPublishing.Publisher.PublishEventsAsync(count);
```

**Why This Is Wrong:**
- Creates coupling between test projects
- Test infrastructure is not meant to be reused across projects
- Violates VSA principle of slice independence
- Makes test projects dependent on internal implementation details of other test projects

**Correct Pattern (DO THIS):**
```csharp
// ✅ GOOD: Reference the production library directly
<ProjectReference Include="..\PerformanceTester.EventPublishing\..." />

// In test infrastructure (e.g., EventConsumingSystem.cs):
private IHost? _eventPublisherHost;
public IEventPublisher EventPublisher { get; private set; } = null!;

protected override void InitializeSystem()
{
    // Create production services via DI
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddEventPublishing(base.RabbitMQ.ConnectionString);
    _eventPublisherHost = builder.Build();

    // Resolve production interface
    this.EventPublisher = _eventPublisherHost.Services.GetRequiredService<IEventPublisher>();

    // Initialize if needed (e.g., ConnectAsync for RabbitMQ)
    var publisher = _eventPublisherHost.Services.GetRequiredService<RabbitMqPublisher>();
    publisher.ConnectAsync().GetAwaiter().GetResult();
}
```

**Benefits of Correct Pattern:**
- ✅ No test-to-test coupling
- ✅ Tests use production code (better integration testing)
- ✅ Follows VSA principles (slices remain independent)
- ✅ Exposes real API that production consumers will use

**When You Need Services from Another Slice in Tests:**
1. **First choice:** Reference the production library, not the test library
2. Create your own test infrastructure setup using the production API
3. Use DI to initialize production services in your test System class
4. Never expose or depend on another slice's test infrastructure

**Example Use Case:**
EventConsuming tests need to publish events to test consumption. Instead of depending on EventPublishing.IntegrationTests infrastructure, they:
1. Reference `PerformanceTester.EventPublishing` (production)
2. Create `IHost` with `AddEventPublishing()` (production DI setup)
3. Resolve `IEventPublisher` (production interface)
4. Use the same API that production code will use

This ensures EventConsuming tests are testing against the real EventPublishing API, not a test wrapper.

### Running Specific Tests

```bash
# Run single test class
dotnet test --filter "FullyQualifiedName~EventPublisherTests"

# Run single test method
dotnet test --filter "FullyQualifiedName~EventPublisherTests.PublishEventsAsync_ShouldPublishCorrectNumberOfEvents"

# Run all tests in a project
dotnet test src/PerformanceTester.Infrastructure.IntegrationTests
```

### Building Individual Projects

```bash
# Build specific project
dotnet build src/PerformanceTester.Infrastructure

# Build in Release mode
dotnet build -c Release

# Clean build artifacts
dotnet clean
```

### Troubleshooting Integration Tests

**Problem: "Docker endpoint not responding"**
```bash
# Check Docker is running
docker ps

# Restart Docker daemon
sudo systemctl restart docker  # Linux
# or use Docker Desktop (Windows/Mac)
```

**Problem: "Testcontainers timeout after 30 seconds"**
- Docker may be slow pulling images on first run
- Check Docker logs: `docker logs <container_id>`
- Increase timeout in ContainerManager if needed

**Problem: "Port already in use"**
- Testcontainers assigns random ports automatically
- Check for orphaned containers: `docker ps -a`
- Clean up: `docker rm -f $(docker ps -aq)`

**Problem: "RabbitMQ connection refused"**
- Ensure RabbitMQ container health check passed
- ContainerManager waits for health check before returning
- Check logs in test output (xUnit integration enabled)

### Viewing Test Output with Logs

Integration tests capture structured logs via xUnit output:

```bash
# Run tests with verbose output to see logs
dotnet test --logger "console;verbosity=detailed"
```

Example output:
```
[2025-01-10 12:34:56] ℹ️ ContainerManager: Starting PostgreSQL container...
[2025-01-10 12:34:57] ✓ ContainerManager: PostgreSQL healthy (localhost:54321)
[2025-01-10 12:34:57] ℹ️ ContainerManager: Starting RabbitMQ container...
[2025-01-10 12:35:00] ✓ ContainerManager: RabbitMQ healthy (localhost:54322)
```

## Key Design Decisions

### Why Integration-First Testing?

- **Real dependencies:** Uses actual PostgreSQL and RabbitMQ containers (via Testcontainers)
- **No mocking:** Tests verify real behavior, not mock implementations
- **Confidence:** If integration tests pass, code works in production
- **Trade-off:** Slower tests (~1-2 minutes) vs unit tests (~seconds)

**When to use unit tests:**
- Pure logic functions (no I/O)
- Complex algorithms
- Edge cases

**When to use integration tests:**
- Database operations
- RabbitMQ publishing/consuming
- External process interactions
- Service discovery

### Why Testcontainers?

- **No manual setup:** Containers start/stop automatically
- **Isolated:** Each test run gets fresh containers
- **CI-friendly:** Works in GitHub Actions, GitLab CI, etc.
- **Realistic:** Tests use real PostgreSQL/RabbitMQ, not in-memory fakes

### Why Vertical Slice Architecture?

**Alternative Considered:** Traditional layered architecture (Core → Infrastructure → Application)

**Why VSA is Better Here:**

1. **Self-contained capabilities:** Each slice does one thing (publish events, monitor processes, etc.)
2. **Producer-owned contracts:** EventPublishing owns CloudEvent model, consumers depend on it
3. **Parallel development:** 6 slices in Phase 2 can be built simultaneously
4. **Better testability:** Each slice has isolated integration tests
5. **No coupling:** No central "Core" project that everything depends on

**Trade-offs:**
- ❌ More projects in solution (14 projects vs ~5 in layered)
- ✅ But each project is smaller and focused
- ❌ Some duplication (each slice registers its own DI)
- ✅ But no coupling through shared layer

### Container Lifecycle Philosophy

**Key Decision:** Containers start once and never stop during test session.

**Why?**
- PostgreSQL startup: ~5-10 seconds
- RabbitMQ startup: ~8-12 seconds
- Total: ~20 seconds per test if restarted each time
- With singleton: Only 20 seconds for entire test suite

**How?**
- `ContainerManager` is singleton (one instance per test run)
- `EnsureStartedAsync()` is idempotent (safe to call multiple times)
- First test pays startup cost, subsequent tests reuse containers

**Trade-off:**
- ❌ Tests not 100% isolated (share containers)
- ✅ But each test gets fresh DI container
- ✅ Test execution time reduced from minutes to seconds

### Why No Hardcoded Connection Strings?

**Design Principle:** All infrastructure projects accept connection strings as constructor parameters (via DI).

**Benefits:**
- ✅ Testable (inject Testcontainers connection strings)
- ✅ Configurable (production uses appsettings.json)
- ✅ Secure (no credentials in code)

**Pattern:**
```csharp
// DI registration
builder.Services.AddInfrastructure(
    postgresConnectionString: configuration["ConnectionStrings:Postgres"],
    rabbitMqConnectionString: configuration["ConnectionStrings:RabbitMQ"]
);

// In tests
builder.Services.AddInfrastructure(
    postgresConnectionString: postgresContainer.GetConnectionString(),
    rabbitMqConnectionString: $"amqp://admin:admin@{rabbitContainer.Hostname}:{rabbitContainer.GetMappedPublicPort(5672)}"
);
```

## Code Quality Standards

**See also:** [CODING_GUIDELINES.md](CODING_GUIDELINES.md) for functional architecture principles (static classes, explicit parameters, toolbox pattern, vertical slice ownership).

### Zero Warnings Policy

All projects must compile with **zero warnings**. This is enforced in CI/CD.

**Why?**
- Warnings indicate potential bugs or code quality issues
- Warnings are noise that hide real problems
- "Broken windows" theory: tolerance of warnings leads to more warnings

**Common Warnings to Fix:**

**.NET Specific:**
```csharp
// ❌ Warning CS8618: Non-nullable field must contain non-null value when exiting constructor
public string Name { get; set; }

// ✅ Fix: Make nullable or initialize
public string Name { get; set; } = string.Empty;
// or
public string? Name { get; set; }
```

```csharp
// ❌ Warning CS1998: Async method lacks 'await' operators
public async Task DoSomethingAsync() { DoSomething(); }

// ✅ Fix: Remove async or add await
public Task DoSomethingAsync() { DoSomething(); return Task.CompletedTask; }
```

**When to Suppress (Rarely):**
```csharp
// Only when genuinely justified with clear documentation
#pragma warning disable CS1591 // Missing XML comment for publicly visible type
public class InternalHelper { }
#pragma warning restore CS1591
```

### Nullable Reference Types

All projects enable nullable reference types (NRT):

```xml
<PropertyGroup>
  <Nullable>enable</Nullable>
</PropertyGroup>
```

**Benefits:**
- Compile-time null safety
- Fewer NullReferenceExceptions at runtime
- Self-documenting (nullable intent explicit)

**Usage:**
```csharp
// Non-nullable (guaranteed non-null)
public string Name { get; set; } = string.Empty;

// Nullable (may be null)
public string? Description { get; set; }

// Null-forgiving operator (when you know it's not null)
var value = possiblyNull!.ToString();
```

### Naming Conventions

Follow .NET naming conventions:

- **Classes/Interfaces/Methods:** PascalCase (`EventPublisher`, `IEventPublisher`, `PublishEventsAsync`)
- **Parameters/Local Variables:** camelCase (`connectionString`, `eventCount`)
- **Constants:** PascalCase (`MaxRetryAttempts`)
- **Private Fields:** `_camelCase` (`_connection`, `_logger`)
- **Async Methods:** Suffix with `Async` (`PublishEventsAsync`, not `PublishEvents`)

### Test Naming Conventions

Follow xUnit best practices:

```csharp
// Pattern: MethodName_Scenario_ExpectedBehavior
[Fact]
public async Task PublishEventsAsync_WithValidCount_ShouldPublishCorrectNumberOfEvents()
{
    // Arrange
    var count = 100;

    // Act
    var metrics = await System.EventPublisher.PublishEventsAsync(count);

    // Assert
    metrics.EventCount.Should().Be(count);
}
```

**Why This Pattern?**
- ✅ Test intent clear from name
- ✅ Failure messages self-documenting
- ✅ Easy to find specific test

### Asserting.That Pattern (Custom Assertion Extensions)

This project uses a custom fluent assertion pattern via `Asserting.That()` for testing infrastructure classes.

**Core Principle:** **Always extend the infrastructure class itself, not the values it returns.**

**Why This Pattern?**
- ✅ Better encapsulation (assertions access infrastructure directly)
- ✅ Single responsibility (infrastructure class manages its own validation)
- ✅ Type safety (compile-time checking of assertion target)
- ✅ Consistency (all assertions follow same pattern)
- ✅ Readability (test intent clear from assertion method name)

---

#### Pattern 1: Asserting on Values Returned by Infrastructure

**❌ INCORRECT - Asserting on the return value:**

```csharp
// DON'T: Get return value first, then assert on it
var samples = System.EventConsuming.MetricsCollector.GetThroughputSamples();
Asserting.That(samples).HasSamples();  // Wrong! Asserting on return value
```

**✅ CORRECT - Assert on the infrastructure class:**

```csharp
// DO: Assert directly on the infrastructure class
await Asserting.That(System.EventConsuming.MetricsCollector).HasThroughputSamples();
```

**Implementation:**

```csharp
public static class EventConsumingAssertions
{
    /// <summary>
    /// Asserts that throughput samples were collected.
    /// </summary>
    public static async Task HasThroughputSamples(
        this AssertingThat<IMetricsCollector> assertingThat)
    {
        // Get the value internally (not exposed to caller)
        var samples = assertingThat.InstanceToAssert.GetThroughputSamples();

        // Perform assertions
        Assert.NotNull(samples);
        Assert.NotEmpty(samples);
    }
}
```

**Key Points:**
- Extension method operates on `AssertingThat<IMetricsCollector>` (the infrastructure interface)
- Gets return value internally via `assertingThat.InstanceToAssert`
- Caller never sees the intermediate value
- Returns `Task` (not `Task<TResult>`)

---

#### Pattern 2: Asserting on Exceptions with Complex Validation

**❌ INCORRECT - Using raw Assert.ThrowsAsync:**

```csharp
// DON'T: Use raw xUnit assert with manual message validation
var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
{
    await System.EventConsuming.Consumer.StartTrackingEventsAsync(
        expectedCount: 10,
        inactivityTimeout: TimeSpan.FromSeconds(2));
});
Assert.Contains("Inactivity timeout expired", exception.Message);
Assert.Contains("0/10", exception.Message);
```

**✅ CORRECT - Encapsulate in assertion extension:**

```csharp
// DO: Encapsulate exception validation in custom assertion
await Asserting.That(System.EventConsuming.Consumer)
    .ThrowsTimeoutExceptionAfterStartTrackingEvents(
        expectedCount: 10,
        inactivityTimeout: TimeSpan.FromSeconds(2),
        expectedReceivedCount: 0);
```

**Implementation:**

```csharp
public static class EventConsumingAssertions
{
    /// <summary>
    /// Asserts that StartTrackingEventsAsync throws TimeoutException with expected details.
    /// </summary>
    public static async Task ThrowsTimeoutExceptionAfterStartTrackingEvents(
        this AssertingThat<IEventConsumer> assertingThat,
        int expectedCount,
        TimeSpan inactivityTimeout,
        int expectedReceivedCount)
    {
        // Encapsulate the complex assertion logic
        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await assertingThat.InstanceToAssert.StartTrackingEventsAsync(
                expectedCount: expectedCount,
                inactivityTimeout: inactivityTimeout);
        });

        Assert.Contains("Inactivity timeout expired", exception.Message);
        Assert.Contains($"{expectedReceivedCount}/{expectedCount}", exception.Message);
    }
}
```

**Key Points:**
- Extension method operates on `AssertingThat<IEventConsumer>` (the infrastructure interface)
- Encapsulates both exception type validation AND message content validation
- Provides clear, domain-specific assertion name
- Reusable across multiple tests

---

#### Pattern 3: Asserting on Simple Exceptions

**❌ INCORRECT - Using raw Assert.ThrowsAsync:**

```csharp
// DON'T: Use raw xUnit assert even for simple cases
await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
{
    await System.EventConsuming.Consumer.StartTrackingEventsAsync(
        expectedCount: 0,  // Invalid
        inactivityTimeout: TimeSpan.FromSeconds(30));
});
```

**✅ CORRECT - Encapsulate in assertion extension:**

```csharp
// DO: Create specific assertion even for simple validation
await Asserting.That(System.EventConsuming.Consumer)
    .ThrowsArgumentOutOfRangeExceptionForInvalidCount(invalidCount: 0);
```

**Implementation:**

```csharp
public static class EventConsumingAssertions
{
    /// <summary>
    /// Asserts that StartTrackingEventsAsync throws ArgumentOutOfRangeException for invalid count.
    /// </summary>
    public static async Task ThrowsArgumentOutOfRangeExceptionForInvalidCount(
        this AssertingThat<IEventConsumer> assertingThat,
        int invalidCount)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await assertingThat.InstanceToAssert.StartTrackingEventsAsync(
                expectedCount: invalidCount,
                inactivityTimeout: TimeSpan.FromSeconds(30));
        });
    }
}
```

**Key Points:**
- Even simple exception assertions should be encapsulated
- Provides clear test intent through method name
- Keeps test code clean and readable
- Makes it easy to add message validation later if needed

---

#### General Guidelines

**When to Create Assertion Extensions:**

1. ✅ **Always** when asserting on values returned by infrastructure methods
2. ✅ **Always** when asserting on exceptions thrown by infrastructure methods
3. ✅ **Always** when validation logic is complex (multiple Assert calls)
4. ✅ **Always** when the same assertion is used in multiple tests

**Assertion Extension Checklist:**

- [ ] Extends `AssertingThat<TInfrastructure>` (not return value types)
- [ ] Returns `Task` (not `Task<TResult>`)
- [ ] Has clear, domain-specific name describing what it validates
- [ ] Has XML documentation explaining what it asserts
- [ ] Located in test project (e.g., `EventConsumingAssertions.cs`)
- [ ] Uses `assertingThat.InstanceToAssert` to access infrastructure
- [ ] Encapsulates all validation logic (caller doesn't see intermediate values)

**Reference:** See [02.INTEGRATION_TEST_DESIGN.md](docs/02.INTEGRATION_TEST_DESIGN.md) for the complete architectural rationale behind this pattern.

## Dependencies & Versions

### Required Tools

- **.NET 9.0 SDK** (minimum version: 9.0.0)
- **Docker** (minimum version: 20.10)
- **Docker Compose** (for infrastructure)

### Key NuGet Packages

**Phase 0 (IntegrationTesting):**
- Testcontainers 3.11.0
- xUnit 2.6.6
- Microsoft.Extensions.Logging 9.0.0

**Phase 1 (Infrastructure):**
- Npgsql 9.0.3
- RabbitMQ.Client 7.0.0
- Microsoft.Extensions.DependencyInjection 9.0.0

**Phase 2 (EventPublishing):**
- CloudNative.CloudEvents 2.8.0
- CloudNative.CloudEvents.SystemTextJson 2.8.0
- RabbitMQ.Client 7.0.0
- Polly 8.0.0

**Testing (all test projects):**
- xUnit 2.6.6
- FluentAssertions 7.0.0
- Microsoft.Extensions.Hosting 9.0.0

### Docker Infrastructure

Required containers (managed by parent project):

```bash
# Start infrastructure (from /workspace)
docker-compose -f scripts/infrastructure/docker-compose.yml up -d

# Services:
# - PostgreSQL: localhost:5432 (admin/admin)
# - RabbitMQ: localhost:5672 (admin/admin)
# - RabbitMQ Management: http://localhost:15672
```

**Note:** Integration tests use separate Testcontainers (not these). These are for manual testing.

## Relationship to Parent Project

### Parent Project Structure

```
/workspace/                           # Root of performance comparison project
├── CLAUDE.md                         # Parent project guidance (11 languages)
├── implementations/                  # 11 microservice implementations
│   ├── bun/                         # Bun + Prisma (port 8090)
│   ├── dotnet9/                     # .NET 9 JIT + EF Core (port 8092)
│   ├── dotnet9aot/                  # .NET 9 AOT + raw SQL (port 8093)
│   ├── go/                          # Go + GORM (port 8094)
│   ├── java21springboot/            # Spring Boot JVM (port 8097)
│   ├── python/                      # Python + SQLAlchemy (port 8099)
│   ├── rust/                        # Rust + Actix-web (port 8100)
│   └── ...                          # 4 more implementations
├── performance-tester/              # Original Python testing tools
│   ├── service-tester.py            # Performance test orchestrator
│   ├── compare_test_results.py      # Comparison reports
│   └── test-results/                # Test outputs
├── performance-tester-dotnet/       # THIS PROJECT (you are here)
│   ├── CLAUDE.md                    # This file
│   ├── README.md                    # Quick overview
│   ├── docs/                        # Comprehensive design docs
│   └── src/                         # .NET 9 implementation
└── scripts/                         # Build/test automation
    ├── infrastructure/              # Docker Compose, database setup
    └── tools/                       # Cross-language testing scripts
```

### How This Project Fits In

**Parent Project:** Tests 11 identical microservices across different languages/frameworks to compare performance.

**This Project:** Provides .NET-specific tooling for:
1. **Integration testing infrastructure** - Shared across all .NET test projects
2. **Service discovery and management** - Find and manage running services
3. **Event publishing utilities** - CloudEvents-compliant RabbitMQ publishing
4. **Future:** Complete performance testing suite (rewriting Python tools in .NET)

**Why Separate?**
- Parent CLAUDE.md focuses on 11-language comparison
- This CLAUDE.md focuses on .NET-specific development
- Keeps documentation focused and manageable
- Allows independent versioning

### When to Use Which CLAUDE.md?

**Use `/workspace/CLAUDE.md` when:**
- Working with any of the 11 microservice implementations
- Understanding overall performance comparison strategy
- Running cross-language tests
- Setting up infrastructure (Docker, databases)

**Use `/workspace/performance-tester-dotnet/CLAUDE.md` (this file) when:**
- Building/testing .NET infrastructure libraries
- Adding new slices (EventConsuming, ProcessMonitoring, etc.)
- Troubleshooting integration tests
- Understanding VSA architecture decisions

## Reference Documentation

### Design Documentation (docs/)

**Comprehensive Phase Plans** (~270KB total):

1. **[01.TESTING_STRATEGY.md](docs/01.TESTING_STRATEGY.md)** - Integration-first testing philosophy
2. **[02.INTEGRATION_TEST_DESIGN.md](docs/02.INTEGRATION_TEST_DESIGN.md)** - Test patterns and best practices
3. **[03.DOTNET_REWRITE_PLAN.md](docs/03.DOTNET_REWRITE_PLAN.md)** - Complete rewrite plan, architectural decisions
4. **[04.IMPLEMENTATION_ORDER_VSA.md](docs/04.IMPLEMENTATION_ORDER_VSA.md)** - Phase-by-phase implementation order

**Detailed Phase Plans** (docs/plans/):

5. **[05.PHASE_0_INTEGRATION_TESTING_DESIGN.md](docs/plans/05.PHASE_0_INTEGRATION_TESTING_DESIGN.md)** (38KB) - ContainerManager, IntegrationTestBase design
6. **[06.PHASE_1_INFRASTRUCTURE_PLAN.md](docs/plans/06.PHASE_1_INFRASTRUCTURE_PLAN.md)** (74KB) - ServiceDiscovery, Database, RabbitMQ implementation
7. **[07.PHASE_2_EVENTPUBLISHING_PLAN.md](docs/plans/07.PHASE_2_EVENTPUBLISHING_PLAN.md)** (43KB) - CloudEvents publishing implementation
8. **[08.PHASE_2_EVENTCONSUMING_PLAN.md](docs/plans/08.PHASE_2_EVENTCONSUMING_PLAN.md)** (64KB) - Event consuming with inactivity timeout and throughput tracking

### Project-Specific Documentation

- **[README.md](README.md)** - Quick overview, build/test commands
- **[CODING_GUIDELINES.md](CODING_GUIDELINES.md)** - Functional architecture principles and patterns
- **[src/PerformanceTester.Infrastructure/README.md](src/PerformanceTester.Infrastructure/README.md)** - Infrastructure slice documentation
- **[src/PerformanceTester.EventPublishing/README.md](src/PerformanceTester.EventPublishing/README.md)** - EventPublishing slice documentation

### External Resources

- **Testcontainers .NET:** https://dotnet.testcontainers.org/
- **Vertical Slice Architecture:** https://www.jimmybogard.com/vertical-slice-architecture/
- **CloudEvents Specification:** https://cloudevents.io/
- **RabbitMQ .NET Client:** https://www.rabbitmq.com/tutorials/tutorial-one-dotnet

## Contributing

### Before Making Changes

1. **Read relevant design docs** - Understand why decisions were made
2. **Check existing tests** - Follow established patterns
3. **Ensure Docker is running** - Required for integration tests
4. **Run tests first** - Verify baseline before changes

### Submitting Changes

1. **Zero warnings** - All projects must compile cleanly
2. **All tests passing** - Run `dotnet test` before committing
3. **Follow VSA principles** - Keep slices independent
4. **Add integration tests** - Cover new functionality
5. **Update documentation** - Keep READMEs and CLAUDE.md current

### Questions?

- Check design docs first (docs/)
- Review similar implementations (other slices)
- Look at integration tests (shows intended usage)
- Check parent CLAUDE.md for infrastructure setup

## Summary: What Makes This Project Different

### From Parent Project Perspective

**Parent project** focuses on:
- Comparing 11 language implementations
- Cross-language performance testing
- Infrastructure setup (Docker, databases)
- Build/test orchestration scripts

**This project** focuses on:
- .NET-specific development
- Integration testing best practices
- Reusable .NET libraries
- Modern .NET 9 patterns

### From Traditional .NET Project Perspective

**Traditional .NET project** uses:
- Layered architecture (Core → Infrastructure → Application)
- Central shared models
- Heavy use of interfaces for everything
- Unit tests with mocking

**This project** uses:
- Vertical Slice Architecture (independent slices)
- Producer-owned contracts (no shared models)
- Selective interfaces (only where needed)
- Integration-first testing (real dependencies)

**Why?**
- Better fit for performance testing domain
- Parallel development of slices
- Isolated testing per slice
- No coupling through shared layer

---

**Last Updated:** 2025-01-20
**Status:** Phases 0-4 COMPLETE; Phase 5 (CLI) remaining
**Target Framework:** .NET 9.0
