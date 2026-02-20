# PerformanceTester.DockerMonitoring

**Phase 2e: Docker Container Monitoring** - Monitors Docker container resource usage (CPU, memory) during performance tests.

## Overview

This slice provides real-time Docker container monitoring capabilities using the Docker.DotNet client library. It runs as a BackgroundService during performance tests to track RabbitMQ and PostgreSQL container metrics without impacting test execution.

### Key Features

- **Event-driven monitoring** - Collects a sample directly from each Docker stats push (no polling)
- **Docker.DotNet integration** - Uses official Docker SDK (no subprocess spawning)
- **BackgroundService pattern** - Runs asynchronously during test execution
- **Cross-platform** - Supports Linux (Unix socket) and Windows (named pipe)
- **Producer-owned contract** - Slice owns `DockerMetrics` model (VSA principle)
- **Multiple containers** - Monitor multiple containers simultaneously
- **Named delegates** - Exposes `WarmupDockerMonitorsDelegate`, `StartDockerMonitoringDelegate`, `GetDockerMetricsDelegate` delegates via DI
- **Graceful degradation** - Continues monitoring even if containers aren't found initially

## Usage

### Basic Setup

```csharp
using Microsoft.Extensions.Hosting;
using PerformanceTester.DockerMonitoring;
using PerformanceTester.DockerMonitoring.Monitoring;
using PerformanceTester.Infrastructure.ValueObjects;

var builder = Host.CreateApplicationBuilder();

// Register Docker monitoring for multiple containers in one call
builder.Services.AddDockerMonitoring(
    postgresContainerName,
    rabbitMqContainerName);

var host = builder.Build();

// Resolve named delegates
var warmup = host.Services.GetRequiredService<WarmupDockerMonitorsDelegate>();
var startMonitoring = host.Services.GetRequiredService<StartDockerMonitoringDelegate>();
var getMetrics = host.Services.GetRequiredService<GetDockerMetricsDelegate>();

// Start monitoring (BackgroundServices start automatically)
await host.StartAsync();

// Warm up Docker API connections
await warmup();

// Start collecting metrics (blocks until first sample per container)
await startMonitoring(_ => { });

// ... run your performance tests ...

// Stop monitoring
await host.StopAsync();

// Retrieve collected metrics per container
var metrics = getMetrics(postgresContainerName);
Console.WriteLine($"PostgreSQL: {metrics.Count} samples collected");
```

## API Reference

### Named Delegates

The public API consists of four named delegates registered in DI:

```csharp
// Reports docker monitoring phase changes
public delegate void ReportDockerMonitorProgressDelegate(DockerMonitorPhaseInfo phaseInfo);

// Warms up Docker API for all registered containers
public delegate Task WarmupDockerMonitorsDelegate(CancellationToken ct = default);

// Starts metrics collection on all registered containers
public delegate Task StartDockerMonitoringDelegate(
    ReportDockerMonitorProgressDelegate reportProgress,
    CancellationToken ct = default);

// Retrieves collected metrics for a specific container by name
public delegate IReadOnlyCollection<DockerMetrics> GetDockerMetricsDelegate(
    NonEmptyString containerName);
```

### `DockerMetrics`

Immutable record representing a single container metrics snapshot.

```csharp
public sealed record DockerMetrics
{
    public required DateTime Timestamp { get; init; }
    public required string ContainerId { get; init; }
    public required string ContainerName { get; init; }
    public required double CpuPercent { get; init; }
    public required double MemoryMB { get; init; }
}
```

### `AddDockerMonitoring()`

Extension method for registering Docker monitoring services.

```csharp
public static IServiceCollection AddDockerMonitoring(
    this IServiceCollection services,
    params NonEmptyString[] containerNames)
```

**Parameters:**
- `containerNames` - Names of Docker containers to monitor (as `NonEmptyString` values)

**Returns:** Service collection for fluent chaining.

**Registers:**
- Internal `DockerMonitorService` BackgroundServices (one per container, keyed singletons)
- `WarmupDockerMonitorsDelegate` delegate (singleton)
- `StartDockerMonitoringDelegate` delegate (singleton)
- `GetDockerMetricsDelegate` delegate (singleton)

## CPU Calculation: Container vs Process

**Important:** Docker container CPU calculation differs from process-level CPU calculation.

### Container-Level CPU (This Slice)

```csharp
// Formula: (cpu_delta / system_delta) * cpu_count * 100
var cpuPercent = (double)cpuDelta / systemDelta * cpuCount * 100.0;
```

**Characteristics:**
- Compares container CPU time against **system-wide CPU time** (cgroup accounting)
- **Scales** by multiplying by core count (shows total capacity used)
- Stateless (Docker API provides both current and previous stats in single response)
- Matches Docker CLI (`docker stats`) output exactly

### Process-Level CPU (ProcessMonitoring Slice)

```csharp
// Formula: (CPUTimeDelta / ElapsedTimeDelta / CoreCount) * 100
var cpuPercent = (cpuDelta / timeDelta / Environment.ProcessorCount) * 100.0;
```

**Characteristics:**
- Compares CPU time against **wall-clock time** (real elapsed time)
- **Normalizes** by dividing by core count (shows per-core average)
- Stateful (maintains previous timestamp and CPU time between calls)
- Matches .NET Process API conventions

### Why the Formulas Differ

| Aspect | Process Monitoring | Docker Monitoring |
|--------|-------------------|-------------------|
| **Data Source** | `Process.TotalProcessorTime` | Docker Stats API (cgroups) |
| **Comparison** | CPU time vs wall-clock time | Container CPU vs system CPU |
| **Core Count** | Divide (normalize) | Multiply (scale) |
| **State** | Stateful (maintains previous sample) | Stateless (Docker provides precpu_stats) |
| **Use Case** | Track individual process usage | Track container resource limits |
| **Matches** | .NET Process API conventions | Docker CLI conventions |

**VSA Implication:** These implementations remain separate per Vertical Slice Architecture principles. Each slice owns its CPU calculation with no shared abstraction.

## Architecture

### Folder Structure

```
DockerMonitoring/
├── Connection/               # State machine & reconnection logic
│   ├── ConnectionState.cs       # Discriminated union of connection states
│   ├── ConnectionStateMachine.cs # Pure state transition function
│   ├── StreamEvent.cs           # Stream event discriminated union
│   ├── ReconnectionPolicy.cs    # Backoff and retry decisions
│   └── StreamingConstants.cs    # Timeout and retry configuration
├── Stats/                    # Docker API & metrics conversion
│   ├── DockerClientWrapper.cs   # Docker.DotNet client wrapper
│   └── StatsProcessing.cs      # Raw stats → DockerMetrics conversion
├── Monitoring/               # Lifecycle orchestration & public API
│   ├── DockerMonitorService.cs  # BackgroundService (state machine interpreter)
│   ├── DockerMonitorPhaseInfo.cs # Phase enum & phase info record
│   ├── PhaseReporting.cs        # ConnectionState → PhaseInfo mapping
│   ├── DockerMonitoringDelegates.cs # Public named delegates
│   └── ServiceCollectionExtensions.cs # DI registration
├── ValueObjects/             # Domain value objects
├── DockerMetrics.cs          # Shared slice-level data model
└── README.md
```

### Components

```
Monitoring/DockerMonitorService (internal BackgroundService)
    |  uses
Connection/ConnectionStateMachine (pure state transitions)
    |  uses
Stats/DockerClientWrapper
    |  wraps
Docker.DotNet.DockerClient
    |  produces
DockerMetrics (root)
    |  stores in
ConcurrentBag<DockerMetrics> (in-memory)
    |  retrieved via
Monitoring/GetDockerMetricsDelegate delegate
```

### Design Patterns

**Named Delegates (FP Pattern):**
- `WarmupDockerMonitorsDelegate`, `StartDockerMonitoringDelegate`, `GetDockerMetricsDelegate` registered as singletons in DI
- Consumers resolve only delegates — never interfaces or service instances
- Internal `DockerMonitorService` instances managed via keyed services

**Event-Driven Sampling:**
- Each Docker stats push is collected directly as a sample in `OnStatsReceived()`
- No intermediate buffer or polling timer - eliminates duplicates and missed data
- Sample rate is determined by Docker's push frequency (~1s), not a configured interval

**Graceful Degradation:**
- Returns empty collection if container not found (non-fatal error)
- Logs debug message (not warning, to avoid noise)
- Reports `StreamFailed` phase via `ReportDockerMonitorProgressDelegate`

## Cross-Platform Support

### Linux
- Uses Unix socket: `unix:///var/run/docker.sock`
- Requires Docker daemon running locally

### Windows
- Uses named pipe: `npipe://./pipe/docker_engine`
- Requires Docker Desktop or Docker Engine

### macOS
- Uses Unix socket (same as Linux)
- Requires Docker Desktop

## Performance Characteristics

**Sampling Performance:**
- First sample: ~200-300ms (Docker API call overhead + 100ms delay)
- Subsequent samples: ~200-300ms each (steady state)
- Memory overhead: ~10-20 MB per monitor instance

**Recommendations:**
- Expect approximately 1 sample per second (Docker's default push rate)
- Sample rate is determined by Docker, not configurable on this side

## Dependencies

- **Docker.DotNet** 3.125.15 - Official Docker SDK for .NET
- **Microsoft.Extensions.Hosting.Abstractions** 9.0.0 - BackgroundService support
- **Microsoft.Extensions.Logging.Abstractions** 9.0.0 - Structured logging
- **PerformanceTester.Infrastructure** - `NonEmptyString` value object

## Integration Testing

The `PerformanceTester.DockerMonitoring.IntegrationTests` project provides comprehensive integration tests using real Docker containers via Testcontainers.

### Running Tests

```bash
dotnet test src/PerformanceTester.DockerMonitoring.IntegrationTests
```

**Requirements:**
- Docker must be running
- Containers `performance-tester-postgres` and `performance-tester-rabbitmq` must exist
- Tests use Testcontainers to manage container lifecycle

**Test Coverage:**
- BackgroundService lifecycle (start/stop)
- Metrics collection on each Docker push
- Container not found handling (graceful degradation)
- CPU calculation validation
- Memory calculation validation
- Chronological ordering of metrics

## Known Limitations

- **Container name matching:** Exact match required (no wildcards)
- **Single Docker daemon:** Multi-host not supported
- **Sample rate:** Determined by Docker's push frequency (~1s), not configurable

## Related Slices

- **Phase 2c: ProcessMonitoring** - Monitors individual process resource usage
- **Phase 3: Reporting** - Aggregates and visualizes all collected metrics
- **Phase 4: Orchestration** - Coordinates all monitoring slices during tests

## References

- [Docker.DotNet GitHub](https://github.com/dotnet/Docker.DotNet)
- [Docker Stats API Documentation](https://docs.docker.com/engine/api/v1.43/#tag/Container/operation/ContainerStats)
