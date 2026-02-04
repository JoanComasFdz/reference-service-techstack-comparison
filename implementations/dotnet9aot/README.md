# .NET 9 AOT Reference Service

This is a Native AOT version of the Performance Test service using .NET 9 and ASP.NET Core. It uses raw SQL with NpgsqlDataSource instead of Entity Framework Core for full AOT compatibility.

## Features

- **RabbitMQ Integration**: Subscribes to `instrument.status.changed` events and publishes `instrumentstatus.kpi.updated` events
- **PostgreSQL Database**: Stores instrument status changes using raw SQL with NpgsqlDataSource (for AOT compatibility)
- **REST API**: Provides `/kpi` endpoint to retrieve the latest stored status
- **CloudEvents Support**: Implements CloudEvents v1.0 specification with custom extensions
- **Minimal API**: Uses ASP.NET Core 9.0 minimal API for lightweight implementation
- **Native AOT Compilation**: Compiled to native code for faster startup and lower memory footprint

## Native AOT Benefits

- **Faster Startup**: Near-instantaneous startup times (typically <100ms)
- **Lower Memory Usage**: Reduced memory footprint compared to JIT compilation
- **No JIT Overhead**: Code is pre-compiled, no runtime compilation needed
- **Self-Contained Executable**: No .NET runtime required on target machine
- **Better Performance**: Optimized native code execution

## Architecture

The service consists of:
- **Models**:
  - `CloudEvent` and `InstrumentStatus` data models
  - `AppJsonSerializerContext`: Source-generated JSON serialization for AOT compatibility
- **Services**:
  - `InstrumentStatusRepository`: Raw SQL repository using NpgsqlDataSource
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

### 2. Install Native Compilation Tools

Native AOT requires platform-specific build tools:

#### Linux (Ubuntu/Debian)
```bash
# Install clang and zlib development headers
sudo apt-get install -y clang zlib1g-dev
```

### 3. Infrastructure Requirements

Ensure Docker services are running:
```bash
# From the project root directory
cd ..
docker-compose up -d
```

This starts:
- RabbitMQ on port 5672 (Management UI on 15672)
- PostgreSQL on port 5432

## Building the Service with AOT

### Standard Build (for development/testing)
```bash
cd dotnet9aot
dotnet restore
dotnet build
```

### Native AOT Compilation (Release Build)

The AOT compilation process is more involved and takes longer than standard builds:

```bash
cd dotnet9aot

# Publish with Native AOT
dotnet publish -c Release
```

This will:
1. Restore NuGet packages
2. Compile the C# code to IL
3. Run ahead-of-time compilation to native code
4. Trim unused code
5. Link native dependencies
6. Create a self-contained executable

**Build Time**: First AOT build typically takes 1-3 minutes depending on system specs.

### Platform-Specific AOT Builds

#### Linux x64
```bash
dotnet publish -c Release -r linux-x64
```

## Running the Service

### Option 1: Run with dotnet (Development)

```bash
cd dotnet9aot
dotnet run
```

This runs the service for development. The service will start on **port 8093** (HTTP).

### Option 2: Run Pre-built DLL (Debug/Release)

```bash
cd dotnet9aot

# Build first
dotnet build -c Release

# Run the DLL
dotnet bin/Release/net9.0/Dotnet9ReferenceServiceAoT.dll
```

### Option 3: Run Native AOT Executable (Production)

After publishing with AOT, run the native executable:

#### Linux
```bash
cd dotnet9aot
./bin/Release/net9.0/linux-x64/publish/dotnet9AotReferenceService
```

The service will start on **port 8093** (HTTP).

**Note**: The native executable is self-contained and doesn't require .NET runtime to be installed on the target machine.

## Testing the Service

### 1. Verify the service is running

Check the logs for startup messages:
```
info: Dotnet9ReferenceServiceAoT.Services.RabbitMqService[0]
      RabbitMQ initialized: Exchange=referenceservice.comparison, Queue=dotnet9aot
info: Dotnet9ReferenceServiceAoT.Services.RabbitMqService[0]
      Started consuming messages from queue: dotnet9aot
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
curl http://localhost:8093/kpi | python3 -m json.tool
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

From the implementations/dotnet9aot directory:
```bash
cd ../../performance-tester-dotnet
dotnet run --project src/PerformanceTester.Cli -- test --port 8093
```

## Configuration

Configuration is managed via `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=dotnet9aot_db;Username=admin;Password=admin"
  }
}
```

You can override settings using:
- **appsettings.Development.json** (for development)
- **Environment variables**: `ConnectionStrings__DefaultConnection`
- **Command-line arguments**: `dotnet run --ConnectionStrings:DefaultConnection="..."`

## RabbitMQ Configuration

The service uses the following RabbitMQ settings:
- **Exchange**: `referenceservice.comparison` (topic exchange)
- **Queue**: `dotnet9aot` (durable)
- **Routing Key (Subscribe)**: `instrument.status.changed`
- **Routing Key (Publish)**: `instrumentstatus.kpi.updated`
- **Connection**: `localhost:5672` (admin/admin)

## Database Schema

The service automatically creates the `dotnet9aot_instrument_status` table using raw SQL:

| Column           | Type      | Description                          |
|------------------|-----------|--------------------------------------|
| id               | bigserial | Primary key (auto-increment)         |
| device_id        | text      | Device identifier                    |
| previous_status  | text      | Previous device status               |
| current_status   | text      | Current device status                |
| timestamp        | timestamp | When the status change was recorded  |

## Project Structure

```
dotnet9aot/
├── CloudEvent.cs                        # CloudEvents v1.0 model
├── InstrumentStatus.cs                  # Database entity
├── AppJsonSerializerContext.cs          # AOT JSON serialization context
├── InstrumentStatusRepository.cs        # Raw SQL repository
├── IInstrumentStatusRepository.cs       # Repository interface
├── RabbitMqService.cs                   # RabbitMQ background service
├── Program.cs                           # Main application entry point
├── appsettings.json                     # Configuration (port 8093)
├── appsettings.Development.json
├── Dotnet9ReferenceServiceAoT.csproj    # Project file (with PublishAot=true)
└── README.md
```

## Dependencies

- **RabbitMQ.Client 7.1.2**: RabbitMQ client library for .NET (AOT-compatible)
- **Npgsql 9.0.4**: PostgreSQL driver for .NET (used for raw SQL, fully AOT-compatible)

## AOT-Specific Implementation Details

### JSON Serialization Context

To support Native AOT, we use source-generated JSON serialization instead of reflection:

```csharp
[JsonSerializable(typeof(List<InstrumentStatus>))]
[JsonSerializable(typeof(InstrumentStatus))]
[JsonSerializable(typeof(CloudEvent))]
[JsonSerializable(typeof(Dictionary<string, object>))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}
```

This generates JSON serialization code at compile-time instead of using reflection at runtime.

### Project Configuration

Key settings in `Dotnet9ReferenceServiceAoT.csproj`:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>false</InvariantGlobalization>
</PropertyGroup>
```

- `PublishAot=true`: Enables Native AOT compilation (this service DOES use AOT)
- `InvariantGlobalization=false`: Allows culture-specific formatting (required for PostgreSQL)

## Performance Comparison

Compared to the standard .NET 9 version (`dotnet9`):

| Metric                | dotnet9 (JIT) | dotnet9aot (Native AOT) | Improvement |
|-----------------------|---------------|-------------------------|-------------|
| Startup Time          | ~2-3 seconds  | ~50-100ms               | **20-60x**  |
| Memory at Startup     | ~80-120 MB    | ~40-60 MB               | **2x**      |
| First Request Latency | Higher (JIT)  | Lower (pre-compiled)    | **Better**  |
| Deployment Size       | Smaller       | Larger (self-contained) | Trade-off   |

## AOT Implementation Details

**This service is fully AOT-compatible** using raw SQL instead of Entity Framework Core:

Implementation approach:
- ✅ Source-generated JSON serialization (AppJsonSerializerContext)
- ✅ No dynamic reflection in custom code
- ✅ RabbitMQ client is AOT-compatible
- ✅ Raw SQL with NpgsqlDataSource (no EF Core)
- ✅ Fully compiled to native code with PublishAot=true

### Why Raw SQL Instead of EF Core:
Entity Framework Core has limited AOT support and requires compiled models, which adds complexity. This implementation uses NpgsqlDataSource with raw SQL for maximum AOT compatibility and simplicity.

## Limitations of Native AOT

- **No EF Core**: This service uses raw SQL instead of Entity Framework Core
- **Longer Build Times**: AOT compilation takes significantly longer than regular builds
- **Limited Reflection**: Dynamic reflection is restricted (we use source generators instead)
- **Larger Binaries**: Self-contained executables are larger (30-100 MB)
- **Platform-Specific**: Must compile separately for each target platform

## Troubleshooting

### AOT Build Errors

If you encounter AOT-specific warnings or errors:

```bash
# Clean and rebuild
dotnet clean
dotnet publish -c Release
```

Common AOT warnings can often be safely ignored if the application runs correctly.

### Service won't start
- Ensure RabbitMQ and PostgreSQL are running: `docker-compose ps`
- Check port 8093 is not already in use: `lsof -i :8093` (Linux/macOS) or `netstat -ano | findstr :8093` (Windows)

### Database connection errors
- Verify PostgreSQL is accessible: `psql -h localhost -U admin -d dotnet9aot_db`
- Check connection string in `appsettings.json`

### RabbitMQ connection errors
- Verify RabbitMQ is running: Visit http://localhost:15672
- Check credentials (default: admin/admin)

### No events received
- Check RabbitMQ exchange and queue exist in the management UI
- Verify routing key bindings
- Check service logs for error messages

### AOT Runtime Errors

If the AOT executable crashes at runtime:
1. Check that all required native libraries are installed (`clang`, `zlib1g-dev`)
2. Verify the executable has proper permissions (`chmod +x` on Linux/macOS)
3. Try running with `DOTNET_EnableDiagnostics=1` environment variable for debugging

## Stopping the Service

Press `Ctrl+C` in the terminal where the service is running.

## Comparison with Java Implementations

This .NET 9 AOT implementation provides:
- **Faster startup than JVM** (including GraalVM Native Image)
- **Lower memory footprint** than traditional Java implementations
- **Simpler build process** than GraalVM Native Image
- **Better developer experience** with faster iteration times in development (JIT mode)
- **Cross-platform support** with excellent Linux, Windows, and macOS support
- **Modern language features** with C# 12 and .NET 9

## Additional Resources

- [.NET 9 Documentation](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9/overview)
- [Native AOT Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [ASP.NET Core with Native AOT](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot)
- [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/)
- [RabbitMQ .NET Client](https://www.rabbitmq.com/tutorials/tutorial-one-dotnet)
