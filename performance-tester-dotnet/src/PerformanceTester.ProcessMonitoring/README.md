# PerformanceTester.ProcessMonitoring

Continuous process resource monitoring (CPU, memory, threads) with BackgroundService lifecycle management. Implements Phase 2c of the Performance Tester .NET implementation.

## Features

### Process Monitoring
- CPU usage tracking via Process.TotalProcessorTime delta calculation
- Memory usage (Working Set in MB)
- Thread count monitoring
- Sampling interval: 500ms (configurable)
- PeriodicTimer for accurate interval timing

### CPU Calculation
- Non-blocking CPU sampling (calculates delta between samples)
- Per-core normalization (0-100% per core average)
- Handles multi-core systems correctly
- Formula: `(CPUTimeDelta / ElapsedTimeDelta / CoreCount) * 100`

### BackgroundService Lifecycle
- ProcessMonitorService (BackgroundService) samples process and writes to Channel
- MetricsCollectorService (BackgroundService) reads from Channel and stores in ConcurrentBag
- Both services managed by `IHost.StartAsync()` / `IHost.StopAsync()`
- Graceful shutdown: 60s timeout for channel drain
- Channel completion signal on shutdown

### Data Collection
- Thread-safe via Channel<ProcessMetrics> and ConcurrentBag
- Continuous collection until IHost.StopAsync()
- Orchestrator retrieves via `IProcessMonitor.GetCollectedMetrics()` after test completion

## Installation

Add project reference:
```bash
dotnet add reference ../PerformanceTester.ProcessMonitoring/PerformanceTester.ProcessMonitoring.csproj
```

## Usage

### Dependency Injection Setup

```csharp
using PerformanceTester.ProcessMonitoring;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder();

// Register process monitoring for specific PID
builder.Services.AddProcessMonitoring(
    processId: 1234,
    samplingInterval: TimeSpan.FromMilliseconds(500)
);

// Configure HostOptions for graceful shutdown
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(60);
});

var host = builder.Build();
```

### Monitoring a Process

```csharp
// Start BackgroundServices (monitoring begins automatically)
await host.StartAsync(cancellationToken);

// Run your test workload here
// Monitoring happens in background continuously

// Stop BackgroundServices (allows metrics collection to complete)
await host.StopAsync(cancellationToken);

// Retrieve collected metrics
var monitor = host.Services.GetRequiredService<IProcessMonitor>();
var metrics = monitor.GetCollectedMetrics();

Console.WriteLine($"Collected {metrics.Count} process metrics");
foreach (var metric in metrics)
{
    Console.WriteLine($"{metric.Timestamp:HH:mm:ss} - " +
        $"CPU: {metric.CpuPercent:F2}%, " +
        $"RAM: {metric.MemoryMB:F2} MB, " +
        $"Threads: {metric.ThreadCount}");
}
```

### Finding Process ID

Use Infrastructure slice's IServiceDiscovery to find process by port:

```csharp
using PerformanceTester.Infrastructure;

var serviceDiscovery = host.Services.GetRequiredService<IServiceDiscovery>();
var processId = await serviceDiscovery.FindServiceProcessIdAsync(
    port: 8094,
    timeout: TimeSpan.FromSeconds(30),
    cancellationToken);

// Then monitor that process
builder.Services.AddProcessMonitoring(processId);
```

## Integration Testing

This project includes comprehensive integration tests using self-monitoring pattern:

```bash
# Run all tests
dotnet test PerformanceTester.ProcessMonitoring.IntegrationTests

# Run specific test class
dotnet test --filter "FullyQualifiedName~ProcessMonitorIntegrationTests"
```

Tests validate:
- Metrics collection with real process
- Sampling interval accuracy
- Valid metric data (CPU, memory, threads)
- Chronological timestamps
- BackgroundService lifecycle (start/stop)
- Graceful shutdown and channel completion

## Architecture

### Vertical Slice Architecture (VSA)
- ProcessMonitoring slice owns ProcessMetrics model (producer-owned contract)
- Exposes single interface: `IProcessMonitor`
- No dependencies on other project slices (only System.Diagnostics.Process)

### Design Decisions

**CPU Calculation:**
- Uses Process.TotalProcessorTime delta (not instantaneous CPU%)
- More accurate than Process.TotalProcessorTime.TotalMilliseconds
- Normalizes to per-core average (0-100% per core)
- First sample returns 0.0 (needs previous state for delta)

**PeriodicTimer vs Task.Delay Loop:**
- PeriodicTimer provides accurate interval timing
- Accounts for execution time (not drift over time)
- More efficient than `while(true) { await Task.Delay(...) }`

**Two-Tier Architecture:**
- ProcessMonitorService writes to Channel (producer)
- MetricsCollectorService reads from Channel (consumer)
- Separation of concerns: monitoring vs. storage
- BackgroundService lifecycle manages both services

**Thread Safety:**
- ProcessCpuCalculator uses instance state (not thread-safe by design)
- Single writer to Channel (ProcessMonitorService)
- ConcurrentBag for sample storage (thread-safe reads/writes)
- Channel<T> for producer-consumer communication

**Self-Monitoring Pattern (Tests):**
- Tests monitor current test process (Environment.ProcessId)
- Avoids need for external process or service
- Realistic validation (actual process metrics)

## Dependencies

- Microsoft.Extensions.Hosting.Abstractions 9.0.0
- Microsoft.Extensions.DependencyInjection.Abstractions 9.0.0
- Microsoft.Extensions.Logging.Abstractions 9.0.0
- System.Diagnostics.Process (built-in)

## Python Source Reference

This implementation is based on `performance-tester/process_monitor.py` (231 lines) from the original Python implementation.

Key improvements over Python:
- **PeriodicTimer** (accurate interval timing) vs. time.sleep (drift over time)
- **BackgroundService pattern** (managed lifecycle) vs. daemon thread
- **Channel-based metrics collection** (producer-consumer) vs. shared list with append
- **Process.TotalProcessorTime delta** (accurate CPU) vs. psutil.cpu_percent
- **Strongly-typed ProcessMetrics** (record) vs. dict

## License

Part of the Performance Tester .NET implementation.
