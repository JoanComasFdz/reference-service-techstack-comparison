# .NET 9 Reference Service

This is a reference implementation of the Performance Test service using .NET 9 and ASP.NET Core.

## Features

- **RabbitMQ Integration**: Subscribes to `instrument.status.changed` events and publishes `instrumentstatus.kpi.updated` events
- **PostgreSQL Database**: Stores instrument status changes using Entity Framework Core
- **REST API**: Provides `/kpi` endpoint to retrieve the latest stored status
- **CloudEvents Support**: Implements CloudEvents v1.0 specification with custom extensions
- **Minimal API**: Uses ASP.NET Core 9.0 minimal API for lightweight implementation

## Architecture

The service consists of:
- **Models**: `CloudEvent` and `InstrumentStatus` data models
- **Services**:
  - `AppDbContext`: Entity Framework Core database context
  - `RabbitMqService`: Background service for RabbitMQ message consumption and publishing
- **Program.cs**: Main application entry point with minimal API endpoint

## Prerequisites

### 1. Install .NET 9 SDK

#### Windows
1. Download the .NET 9 SDK installer from [https://dotnet.microsoft.com/download/dotnet/9.0](https://dotnet.microsoft.com/download/dotnet/9.0)
2. Run the installer and follow the prompts
3. Verify installation:
   ```powershell
   dotnet --version
   ```

#### Linux (Ubuntu/Debian)
```bash
# Add .NET backports PPA repository
sudo add-apt-repository ppa:dotnet/backports

# Update package lists
sudo apt-get update

# Install .NET 9 SDK and ASP.NET Core runtime
sudo apt-get install -y dotnet-sdk-9.0
sudo apt-get install -y aspnetcore-runtime-9.0

# Verify installation
dotnet --version
```

#### Linux (Fedora/RHEL/CentOS)
```bash
# Add Microsoft package repository
sudo dnf install dotnet-sdk-9.0

# Verify installation
dotnet --version
```

#### macOS
```bash
# Using Homebrew
brew install --cask dotnet-sdk

# Or download from Microsoft
# Download from https://dotnet.microsoft.com/download/dotnet/9.0

# Verify installation
dotnet --version
```

#### WSL (Windows Subsystem for Linux)
Follow the Linux installation instructions above for your WSL distribution.

### 2. Infrastructure Requirements

Ensure Docker services are running:
```bash
# From the project root directory
cd ..
docker-compose up -d
```

This starts:
- RabbitMQ on port 5672 (Management UI on 15672)
- PostgreSQL on port 5432

## Building the Service

```bash
cd dotnet9
dotnet restore
dotnet build
```

## Running the Service

### Option 1: Using dotnet run (Development)

```bash
cd dotnet9
dotnet run
```

The service will start on **port 8092** (HTTP).

### Option 2: Using compiled binary

```bash
cd dotnet9
dotnet build -c Release
dotnet bin/Release/net9.0/dotnet9ReferenceService.dll
```

### Option 3: Publishing a self-contained executable

```bash
cd dotnet9

# For Linux x64
dotnet publish -c Release -r linux-x64 --self-contained

# For Windows x64
dotnet publish -c Release -r win-x64 --self-contained

# For macOS x64
dotnet publish -c Release -r osx-x64 --self-contained

# For macOS ARM64 (Apple Silicon)
dotnet publish -c Release -r osx-arm64 --self-contained
```

Run the published executable:
```bash
# Linux/macOS
./bin/Release/net9.0/linux-x64/publish/dotnet9ReferenceService

# Windows
bin\Release\net9.0\win-x64\publish\dotnet9ReferenceService.exe
```

## Testing the Service

### 1. Verify the service is running

Check the logs for startup messages:
```
info: Dotnet9ReferenceService.Services.RabbitMqService[0]
      RabbitMQ initialized: Exchange=referenceservice.comparison, Queue=dotnet9
info: Dotnet9ReferenceService.Services.RabbitMqService[0]
      Started consuming messages from queue: dotnet9
```

### 2. Send test events

From the project root directory:
```bash
cd ..
python send_events.py -n 5
```

### 3. Query the KPI endpoint

```bash
# Using curl
curl http://localhost:8092/kpi | python3 -m json.tool

# Or using the provided .http file
# Open test.http in VS Code with REST Client extension
```

Expected response (returns the latest status):
```json
{
  "id": 1,
  "deviceId": "DEVICE-001",
  "previousStatus": "idle",
  "currentStatus": "running",
  "timestamp": "2025-10-07T12:34:56.789Z"
}
```

If no records exist, returns 404 Not Found.

### 4. Run performance tests

From the implementations/dotnet9 directory:
```bash
cd ../../performance-tester-dotnet
dotnet run --project src/PerformanceTester.Cli -- test --port 8092
```

## Configuration

Configuration is managed via `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=dotnet9_db;Username=admin;Password=admin"
  }
}
```

**Database Setup**: The service uses a dedicated database `dotnet9_db`. Ensure this database exists before running the service:

```bash
# Connect to PostgreSQL via Docker
docker exec -it performancetest-postgres psql -U admin -d postgres

# Create the database
CREATE DATABASE dotnet9_db;
```

You can override settings using:
- **appsettings.Development.json** (for development)
- **Environment variables**: `ConnectionStrings__DefaultConnection`
- **Command-line arguments**: `dotnet run --ConnectionStrings:DefaultConnection="..."`

## RabbitMQ Configuration

The service uses the following RabbitMQ settings:
- **Exchange**: `referenceservice.comparison` (topic exchange)
- **Queue**: `dotnet9` (durable)
- **Routing Key (Subscribe)**: `instrument.status.changed`
- **Routing Key (Publish)**: `instrumentstatus.kpi.updated`
- **Connection**: `localhost:5672` (admin/admin)

## Database Schema

The service automatically creates the `dotnet9_instrument_status` table using Entity Framework's `EnsureCreatedAsync()`:

| Column           | Type      | Description                          |
|------------------|-----------|--------------------------------------|
| id               | bigserial | Primary key (auto-increment)         |
| device_id        | text      | Device identifier                    |
| previous_status  | text      | Previous device status               |
| current_status   | text      | Current device status                |
| timestamp        | timestamp | When the status change was recorded  |

## Project Structure

```
dotnet9/
├── AppDbContext.cs         # Entity Framework DbContext
├── RabbitMqService.cs      # RabbitMQ background service
├── InstrumentStatus.cs     # Database entity
├── Program.cs              # Main application entry point
├── appsettings.json        # Configuration
├── appsettings.Development.json
├── Dotnet9ReferenceService.csproj # Project file
├── Dotnet9ReferenceService.sln    # Solution file
└── README.md
```

## Dependencies

- **RabbitMQ.Client 7.0.0**: RabbitMQ client library for .NET
- **Npgsql.EntityFrameworkCore.PostgreSQL 9.0.2**: PostgreSQL provider for EF Core
- **Microsoft.EntityFrameworkCore.Design 9.0.0**: EF Core design-time components

## Performance Considerations

.NET 9 includes several performance improvements:
- **Native AOT compilation support** (for even faster startup and lower memory)
- **Improved JSON serialization** with System.Text.Json
- **Reduced allocation** in async operations
- **Better garbage collection** for high-throughput scenarios

### Enabling Native AOT (Optional)

For maximum performance, you can compile the service with Native AOT:

1. Update `Dotnet9ReferenceService.csproj`:
```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

2. Publish:
```bash
dotnet publish -c Release -r linux-x64
```

Note: Native AOT has some limitations with reflection-based features.

## Troubleshooting

### Service won't start
- Ensure RabbitMQ and PostgreSQL are running: `docker-compose ps`
- Check port 8092 is not already in use: `lsof -i :8092` (Linux/macOS) or `netstat -ano | findstr :8092` (Windows)

### Database connection errors
- Verify PostgreSQL is accessible: `psql -h localhost -U admin -d dotnet9_db`
- Ensure the `dotnet9_db` database exists (see Configuration section)
- Check connection string in `appsettings.json`

### RabbitMQ connection errors
- Verify RabbitMQ is running: Visit http://localhost:15672
- Check credentials (default: admin/admin)

### No events received
- Check RabbitMQ exchange and queue exist in the management UI
- Verify routing key bindings
- Check service logs for error messages

## Stopping the Service

Press `Ctrl+C` in the terminal where the service is running.

## Comparison with Java Implementations

This .NET 9 implementation provides:
- **Simpler dependency management** with NuGet (no Maven/Gradle build complexity)
- **Smaller memory footprint** compared to traditional JVM
- **Faster startup time** especially with Native AOT compilation
- **Modern minimal API** with less boilerplate code
- **Cross-platform** with excellent Linux, Windows, and macOS support

## Additional Resources

- [.NET 9 Documentation](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9/overview)
- [ASP.NET Core Documentation](https://learn.microsoft.com/en-us/aspnet/core/)
- [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/)
- [RabbitMQ .NET Client](https://www.rabbitmq.com/tutorials/tutorial-one-dotnet)
