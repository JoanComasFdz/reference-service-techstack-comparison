# PerformanceTester.Infrastructure

Shared infrastructure utilities for the Performance Tester project. Provides service discovery, database management, and RabbitMQ queue management for integration testing.

## Features

### Service Discovery
- Cross-platform process discovery (finds PID listening on port)
- Linux: Uses `lsof -ti :PORT`
- Windows: Uses PowerShell `Get-NetTCPConnection`
- Configurable timeout with retry logic

### Database Management
- Dynamic table discovery (no hardcoded table names)
- Truncate all tables with CASCADE
- Automatic retry on transient failures (2 attempts, 2s delay)
- Row count verification

### RabbitMQ Management
- Queue discovery and purging
- Continues on individual queue failures
- Connection pooling for efficiency

## Installation

Add project reference:
```bash
dotnet add reference ../PerformanceTester.Infrastructure/PerformanceTester.Infrastructure.csproj
```

## Usage

### Dependency Injection Setup

```csharp
using PerformanceTester.Infrastructure;

var builder = Host.CreateApplicationBuilder();

builder.Services.AddInfrastructure(
    postgresConnectionString: "Host=localhost;Port=5432;Database=postgres;Username=admin;Password=admin",
    rabbitMqConnectionString: "amqp://admin:admin@localhost:5672"
);

var host = builder.Build();
```

### Service Discovery

```csharp
var serviceDiscovery = host.Services.GetRequiredService<IServiceDiscovery>();

var processId = await serviceDiscovery.FindServiceProcessIdAsync(
    port: 8080,
    timeout: TimeSpan.FromSeconds(30)
);

if (processId.HasValue)
{
    Console.WriteLine($"Service found: PID {processId.Value}");
}
```

### Database Cleaning

```csharp
var database = host.Services.GetRequiredService<IDatabase>();

await database.ClearDatabaseAsync("my_test_db");
Console.WriteLine("Database cleared successfully");
```

### RabbitMQ Queue Purging

```csharp
var rabbitMq = host.Services.GetRequiredService<IRabbitMQ>();

await rabbitMq.ClearAllQueuesAsync();
Console.WriteLine("All queues purged");
```

## Architecture

### Vertical Slice Architecture (VSA)
- Infrastructure slice owns its models and implementation
- Exposes three segregated interfaces (Interface Segregation Principle):
  - `IServiceDiscovery` - Process discovery
  - `IDatabase` - PostgreSQL management
  - `IRabbitMQ` - RabbitMQ management

### Design Decisions
- **Strategy pattern for ServiceDiscovery**: Platform detection happens once at DI registration (not per method call). `IProcessFinder` interface with Linux/Windows implementations eliminates runtime conditionals and improves testability
- **No shell scripts**: Uses native .NET APIs for cross-platform compatibility
- **Retry logic**: Handles transient failures automatically
- **Structured logging**: Uses Microsoft.Extensions.Logging with visual symbols (✓, ⚠️, ❌)
- **Thread-safe**: Connection pooling and singleton registration

## Dependencies

- Npgsql 9.0.3
- RabbitMQ.Client 7.0.0
- Microsoft.Extensions.DependencyInjection.Abstractions 9.0.0
- Microsoft.Extensions.Logging.Abstractions 9.0.0

## Testing

This project includes comprehensive integration tests using Testcontainers. See `PerformanceTester.Infrastructure.IntegrationTests` for examples.

## License

Part of the Performance Tester .NET implementation.
