# PerformanceTester.EventPublishing

CloudEvents-compliant event generation and RabbitMQ publishing for performance testing. Implements Phase 2 of the Performance Tester .NET implementation.

## Features

### CloudEvent Generation
- CloudEvents v1.0 specification compliance via official `CloudNative.CloudEvents` library
- Random device ID generation (DEVICE-001 to DEVICE-999)
- Realistic status transitions with 3 states (IDLE, RUNNING, ERROR)
- ISO 8601 timestamp formatting

### RabbitMQ Publishing
- Connection pooling: Single persistent IConnection (expensive), per-operation IChannel (cheap)
- Resilience: Polly retry policy with exponential backoff (3 attempts)
- Exchange: "referenceservice.comparison" (topic, durable)
- Routing key: "instrument.status.changed"
- Delivery mode: Persistent
- Throughput tracking: Events/second calculation

## Installation

Add project reference:
```bash
dotnet add reference ../PerformanceTester.EventPublishing/PerformanceTester.EventPublishing.csproj
```

## Usage

### Dependency Injection Setup

```csharp
using PerformanceTester.EventPublishing;

var builder = Host.CreateApplicationBuilder();
builder.Services.AddEventPublishing(
    rabbitMqConnectionString: "amqp://admin:admin@localhost:5672"
);
var host = builder.Build();
```

### Publishing Events

```csharp
var connect = host.Services.GetRequiredService<ConnectPublisherDelegate>();
var publish = host.Services.GetRequiredService<PublishEventsDelegate>();
var disconnect = host.Services.GetRequiredService<DisconnectPublisherDelegate>();

// Connect to RabbitMQ (must be called before publishing)
await connect(cancellationToken);

// Publish 1000 events as fast as possible
var metrics = await publish(1000, cancellationToken);

Console.WriteLine($"Published {metrics.EventCount} events in {metrics.Duration.TotalSeconds:F2}s");
Console.WriteLine($"Throughput: {metrics.EventsPerSecond:F2} events/sec");

// Gracefully disconnect when done
await disconnect(cancellationToken);
```

## Integration Testing

This project includes comprehensive integration tests using Testcontainers:

```bash
# Run all tests
dotnet test PerformanceTester.EventPublishing.IntegrationTests

# Run specific test class
dotnet test --filter "FullyQualifiedName~EventPublisherTests"
```

Tests use real RabbitMQ container (via Testcontainers) to ensure functionality works with actual message broker.

## Architecture

### Folder Structure

```
PerformanceTester.EventPublishing/
├── CloudEvents/                    # Event model creation & serialization
│   ├── CloudEventFactory.cs        # Creates random CloudEvents for testing
│   ├── DeviceId.cs                 # DEVICE-NNN value object
│   ├── InstrumentStatus.cs         # Idle|Running|Error discriminated union
│   └── StatusTransition.cs         # Previous→Current status pair
├── RabbitMq/                       # Transport infrastructure
│   ├── PublisherContext.cs          # Mutable connection state (G32)
│   ├── PublisherOperations.cs       # Static connection operations (G1, G2)
│   ├── RabbitMqConnectionString.cs  # Connection string value object
│   └── RabbitMqPublisher.cs         # Thin shell publisher (G34)
├── EventPublisher.cs               # Batch publishing orchestrator
├── EventPublishingDelegates.cs     # Public delegate contracts (G12)
├── PublishMetrics.cs               # Public metrics return type
└── ServiceCollectionExtensions.cs  # DI registration
```

### Vertical Slice Architecture (VSA)
- EventPublishing slice owns CloudEvent generation and RabbitMQ publishing
- Public API: three named delegates (`ConnectPublisherDelegate`, `DisconnectPublisherDelegate`, `PublishEventsDelegate`)
- Producer-owned contract: CloudEvent model defined by this slice

### Design Decisions
- **Connection pooling**: Single IConnection (singleton), per-operation IChannel (disposable) follows RabbitMQ best practices
- **CloudEvents library**: Uses official `CloudNative.CloudEvents` for v1.0 compliance (not custom serialization)
- **Polly resilience**: Automatic retry for transient failures with exponential backoff
- **Structured logging**: Uses Microsoft.Extensions.Logging with structured messages
- **Performance metrics**: PublishMetrics record tracks throughput and duration

## Dependencies

- CloudNative.CloudEvents 2.8.0
- CloudNative.CloudEvents.SystemTextJson 2.8.0
- RabbitMQ.Client 7.0.0
- Polly 8.0.0
- Microsoft.Extensions.DependencyInjection.Abstractions 9.0.0
- Microsoft.Extensions.Logging.Abstractions 9.0.0

## Python Source Reference

This implementation is based on `performance-tester/send_events.py` (168 lines) from the original Python implementation.

Key differences:
- Uses official CloudEvents library (Python used custom dict serialization)
- Connection pooling with single persistent connection (Python created connection per batch)
- Polly retry policy (Python had no retry logic)
- Strongly-typed PublishMetrics (Python returned dict)

## License

Part of the Performance Tester .NET implementation.
