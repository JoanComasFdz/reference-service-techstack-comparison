# PerformanceTester.EventConsuming

RabbitMQ CloudEvents consumption with throughput tracking and inactivity timeout. Implements Phase 2 of the Performance Tester .NET implementation.

## Features

### Event Consumption
- CloudEvents v1.0 deserialization via official `CloudNative.CloudEvents` library
- BackgroundService pattern for continuous consumption
- Explicit connection control via `ConnectAsync()` and `DisconnectAsync()` methods
- Manual ACK after processing (ensures message safety)
- Prefetch count: 50 (configurable via constant)
- Queue: "performancetesterdotnet" (configurable via DI)
- Exchange: "referenceservice.comparison" (topic, durable)
- Routing key: "instrument.status.changed"

### Inactivity Timeout (Improvement over Python)
- **Python**: Absolute timeout (120s from start, fails slow-but-progressing services)
- **Ours**: Inactivity timeout (120s since last event, resets on every event)
- Allows slow services to complete while detecting truly stuck services
- Timer-based checking (every 1 second)

### Throughput Tracking
- Samples every 500ms based on events received in time window
- Thread-safe recording via `ThroughputTracker`
- Two-tier architecture:
  - `EventConsumerService` writes to `Channel<EventThroughputSample>`
  - `MetricsCollectorService` reads from channel and stores in `ConcurrentBag<EventThroughputSample>`
- Orchestrator retrieves samples via `IMetricsCollector.GetThroughputSamples()` after test completion

### BackgroundService Lifecycle
- `EventConsumerService` (BackgroundService + IEventConsumer)
- `MetricsCollectorService` (BackGroundService + IMetricsCollector)
- Both services managed by `IHost.StartAsync()` / `IHost.StopAsync()`
- Graceful shutdown: 60s timeout for channel drain

## Installation

Add project reference:
```bash
dotnet add reference ../PerformanceTester.EventConsuming/PerformanceTester.EventConsuming.csproj
```

## Usage

### Dependency Injection Setup

```csharp
using PerformanceTester.EventConsuming;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder();

builder.Services.AddEventConsuming(
    rabbitMqConnectionString: "amqp://admin:admin@localhost:5672",
    queueName: "performancetesterdotnet"
);

// Configure HostOptions for graceful shutdown
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(60);
});

var host = builder.Build();
```

### Consuming Events

```csharp
// Get consumer from DI
var consumer = host.Services.GetRequiredService<IEventConsumer>();

// Explicitly connect to RabbitMQ and start consuming
await consumer.ConnectAsync(cancellationToken);

// Start BackgroundServices (if using BackgroundService pattern)
await host.StartAsync(cancellationToken);

// Start tracking events (returns awaitable Task)
var trackingTask = consumer.StartTrackingEventsAsync(
    expectedCount: 10000,
    inactivityTimeout: TimeSpan.FromSeconds(120),
    cancellationToken);

// Publish events to RabbitMQ (via separate EventPublishing service)
// ...

// Wait for target count to be reached
await trackingTask;

// Stop BackgroundServices (allows metrics collection to complete)
await host.StopAsync(cancellationToken);

// Retrieve throughput samples
var metricsCollector = host.Services.GetRequiredService<IMetricsCollector>();
var samples = metricsCollector.GetThroughputSamples();

Console.WriteLine($"Collected {samples.Count} throughput samples");
foreach (var sample in samples)
{
    Console.WriteLine($"{sample.Timestamp:HH:mm:ss} - {sample.ThroughputEventsPerSecond:F2} events/sec (total: {sample.CumulativeEventCount})");
}

// Gracefully disconnect from RabbitMQ
await consumer.DisconnectAsync(cancellationToken);
```

**Note:** The consumer can work in two modes:
1. **Explicit connection:** Call `ConnectAsync()` before `StartAsync()` for explicit control
2. **Automatic connection:** `ConnectAsync()` is called automatically in `ExecuteAsync()` if not already connected (BackgroundService pattern)

For integration testing and orchestration, explicit connection is recommended for better control over lifecycle.

## Integration Testing

This project includes comprehensive integration tests using Testcontainers:

```bash
# Run all tests (supports parallel execution)
dotnet test PerformanceTester.EventConsuming.IntegrationTests

# Run specific test class
dotnet test --filter "FullyQualifiedName~EventConsumerIntegrationTests"
```

**Parallel Test Execution:**
- Each test uses a unique queue name (generated via GUID) to enable parallel execution
- Tests do not interfere with each other when running concurrently
- This significantly reduces test suite execution time

Tests validate:
- Event consumption with target count
- Inactivity timeout behavior (resets on each event)
- Throughput sample collection
- BackgroundService lifecycle (start/stop)
- Graceful shutdown and channel completion

## Architecture

### Vertical Slice Architecture (VSA)
- EventConsuming slice owns EventThroughputSample model (producer-owned contract)
- Exposes two interfaces: `IEventConsumer`, `IMetricsCollector`
- No dependencies on other project slices (only external NuGet packages)

### Design Decisions

**Inactivity Timeout vs Absolute Timeout:**
- Inactivity timeout resets on EVERY event received
- More flexible for slow-but-progressing services
- Better matches production scenarios

**Two-Tier Metrics Collection:**
- EventConsumerService writes to Channel (producer)
- MetricsCollectorService reads from Channel (consumer)
- Separation of concerns: consuming vs. storage
- BackgroundService lifecycle manages both services

**Thread Safety:**
- `Interlocked.Increment` for event counting
- `ThroughputTracker` uses `lock` for throughput calculation
- `ConcurrentBag` for sample storage
- `Channel<T>` for producer-consumer communication

**Event Tracking Pattern:**
- `TaskCompletionSource` signals completion when target reached
- Non-blocking - orchestrator awaits Task, doesn't poll
- Timer-based inactivity checking (separate thread)

## Dependencies

- CloudNative.CloudEvents 2.8.0
- CloudNative.CloudEvents.SystemTextJson 2.8.0
- RabbitMQ.Client 7.0.0
- Microsoft.Extensions.Hosting.Abstractions 9.0.0
- Microsoft.Extensions.DependencyInjection.Abstractions 9.0.0
- Microsoft.Extensions.Logging.Abstractions 9.0.0

## Python Source Reference

This implementation is based on `performance-tester/service-tester.py` (consumer thread, lines 200-320) from the original Python implementation.

Key improvements over Python:
- **Inactivity timeout** (resets on each event) vs. absolute timeout
- **BackgroundService pattern** (managed lifecycle) vs. daemon thread
- **TaskCompletionSource signaling** (non-blocking) vs. polling with sleep
- **Channel-based metrics collection** (producer-consumer) vs. shared list with lock
- **Strongly-typed EventThroughputSample** (record) vs. dict

## License

Part of the Performance Tester .NET implementation.
