# PerformanceTester.DockerMonitoring

**Phase 2e: Docker Container Monitoring** - Monitors Docker container resource usage (CPU, memory) during performance tests.

## Overview

This slice provides real-time Docker container monitoring capabilities using the Docker.DotNet client library. It runs as a BackgroundService during performance tests to track RabbitMQ and PostgreSQL container metrics without impacting test execution.

### Key Features

- ✅ **Real-time monitoring** - Samples container CPU and memory usage at configurable intervals
- ✅ **Docker.DotNet integration** - Uses official Docker SDK (no subprocess spawning)
- ✅ **BackgroundService pattern** - Runs asynchronously during test execution
- ✅ **Cross-platform** - Supports Linux (Unix socket) and Windows (named pipe)
- ✅ **Producer-owned contract** - Slice owns `DockerMetrics` model (VSA principle)
- ✅ **Multiple containers** - Monitor multiple containers simultaneously
- ✅ **Graceful degradation** - Continues monitoring even if containers aren't found initially

## Usage

### Basic Setup

```csharp
using Microsoft.Extensions.Hosting;
using PerformanceTester.DockerMonitoring;

var builder = Host.CreateApplicationBuilder();

// Register Docker monitoring for PostgreSQL
builder.Services.AddDockerMonitoring(
    containerName: "performance-tester-postgres",
    samplingInterval: TimeSpan.FromSeconds(3)); // Default: 3000ms

// Register Docker monitoring for RabbitMQ
builder.Services.AddDockerMonitoring(
    containerName: "performance-tester-rabbitmq",
    samplingInterval: TimeSpan.FromSeconds(3));

var host = builder.Build();

// Start monitoring (BackgroundServices start automatically)
await host.StartAsync();

// ... run your performance tests ...

// Stop monitoring
await host.StopAsync();

// Retrieve collected metrics
var monitors = host.Services.GetServices<IDockerMonitor>();
foreach (var monitor in monitors)
{
    var metrics = monitor.GetCollectedMetrics();
    Console.WriteLine($"{monitor.ContainerName}: {metrics.Count} samples collected");

    foreach (var metric in metrics)
    {
        Console.WriteLine($"  [{metric.Timestamp:HH:mm:ss}] CPU: {metric.CpuPercent:F2}%, Memory: {metric.MemoryMB:F2} MB");
    }
}
```

### Integration with Tests

```csharp
using Xunit;
using PerformanceTester.DockerMonitoring;

public class MyPerformanceTests
{
    [Fact]
    public async Task MyTest_WithDockerMonitoring()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDockerMonitoring("my-container");
        var host = builder.Build();

        await host.StartAsync();

        // Act - run your test
        await RunMyPerformanceTest();

        // Assert - stop and check metrics
        await host.StopAsync();

        var monitor = host.Services.GetRequiredService<IDockerMonitor>();
        var metrics = monitor.GetCollectedMetrics();

        Assert.NotEmpty(metrics);
        Assert.All(metrics, m => Assert.True(m.CpuPercent >= 0));
        Assert.All(metrics, m => Assert.True(m.MemoryMB > 0));
    }
}
```

## API Reference

### `IDockerMonitor`

Public interface for accessing collected metrics.

```csharp
public interface IDockerMonitor
{
    /// <summary>
    /// Gets the name of the container being monitored.
    /// </summary>
    string ContainerName { get; }

    /// <summary>
    /// Gets all collected metrics for this container in chronological order.
    /// </summary>
    IReadOnlyCollection<DockerMetrics> GetCollectedMetrics();
}
```

### `DockerMetrics`

Immutable record representing a single container metrics snapshot.

```csharp
public sealed record DockerMetrics
{
    /// <summary>
    /// When the metrics were captured (UTC).
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Docker container ID (full SHA256).
    /// </summary>
    public required string ContainerId { get; init; }

    /// <summary>
    /// Human-readable container name.
    /// </summary>
    public required string ContainerName { get; init; }

    /// <summary>
    /// CPU usage percentage (0-100% per core, can exceed 100% on multi-core).
    /// Formula: (cpu_delta / system_delta) * cpu_count * 100
    /// </summary>
    public required double CpuPercent { get; init; }

    /// <summary>
    /// Memory usage in megabytes.
    /// Calculated from stats.MemoryStats.Usage / 1024 / 1024.
    /// </summary>
    public required double MemoryMB { get; init; }
}
```

### `AddDockerMonitoring()`

Extension method for registering Docker monitoring services.

```csharp
public static IServiceCollection AddDockerMonitoring(
    this IServiceCollection services,
    string containerName,
    TimeSpan? samplingInterval = null) // Default: 3000ms
```

**Parameters:**
- `containerName` - Name of the Docker container to monitor (e.g., "performance-tester-rabbitmq")
- `samplingInterval` - How often to sample metrics (optional, default: 3000ms)

**Returns:** Service collection for fluent chaining.

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

**Example:** If container used 100ms of 1000ms system CPU on 4-core system:
```
(100ms / 1000ms) * 4 * 100 = 40% total capacity
```

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

**Example:** If 200ms CPU time used in 1000ms elapsed on 4-core system:
```
(200ms / 1000ms / 4) * 100 = 5% per-core average
```

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

### Components

```
DockerMonitorService (BackgroundService + IDockerMonitor)
    ↓ uses
DockerClientWrapper
    ↓ wraps
Docker.DotNet.DockerClient
    ↓ produces
DockerMetrics
    ↓ stores in
ConcurrentBag<DockerMetrics> (in-memory)
    ↓ retrieved via
IDockerMonitor.GetCollectedMetrics()
```

### Design Patterns

**Single-Class Pattern:**
- `DockerMonitorService` implements both `BackgroundService` and `IDockerMonitor`
- Simpler than channel/consumer pattern for slow sampling intervals (3000ms)
- Writing to `ConcurrentBag<T>` is negligible overhead (~nanoseconds)

**PeriodicTimer Pattern:**
- Uses `PeriodicTimer` (not `Task.Delay` loops) for accurate intervals
- Automatically adjusts for drift
- Cancellation-aware via `CancellationToken`

**Graceful Degradation:**
- Returns null if container not found (non-fatal error)
- Logs debug message (not warning, to avoid noise)
- Continues monitoring (allows late container start)

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

**Why Docker Stats API is Slow:**
- Docker.DotNet uses `IProgress<T>` callback pattern
- 100ms delay built-in to wait for async callback
- Network overhead for Docker API communication

**Recommendations:**
- Use sampling intervals ≥ 3000ms for production (avoid overwhelming Docker API)
- For tests, can use faster intervals (e.g., 500ms) to collect more samples
- Expect 1-3 samples per 5 seconds of monitoring

## Dependencies

- **Docker.DotNet** 3.125.15 - Official Docker SDK for .NET
- **Microsoft.Extensions.Hosting.Abstractions** 9.0.0 - BackgroundService support
- **Microsoft.Extensions.Logging.Abstractions** 9.0.0 - Structured logging

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
- ✅ BackgroundService lifecycle (start/stop)
- ✅ Metrics collection at regular intervals
- ✅ Container not found handling (graceful degradation)
- ✅ CPU calculation validation
- ✅ Memory calculation validation
- ✅ Chronological ordering of metrics

## Known Limitations

- **Container name matching:** Exact match required (no wildcards)
- **Single Docker daemon:** Multi-host not supported
- **No streaming:** Uses snapshot mode (not continuous streaming)
- **First sample delay:** PeriodicTimer waits for first tick before sampling

## Future Enhancements (Out of Scope)

- Support for container name wildcards
- Support for multiple Docker hosts
- Network I/O metrics
- Disk I/O metrics
- Container event stream monitoring
- Real-time streaming (not snapshot mode)

## Related Slices

- **Phase 2c: ProcessMonitoring** - Monitors individual process resource usage
- **Phase 3: Reporting** - Aggregates and visualizes all collected metrics
- **Phase 4: Orchestration** - Coordinates all monitoring slices during tests

## References

- [Docker.DotNet GitHub](https://github.com/dotnet/Docker.DotNet)
- [Docker Stats API Documentation](https://docs.docker.com/engine/api/v1.43/#tag/Container/operation/ContainerStats)
- [Python Reference Implementation](../../performance-tester/container_monitor.py)
- [Phase 2e Implementation Plan](../../docs/plans/10.PHASE_2_DOCKERMONITORING_PLAN.md)
