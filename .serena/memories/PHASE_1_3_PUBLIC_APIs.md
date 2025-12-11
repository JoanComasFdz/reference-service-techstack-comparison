# Phase 1 (Infrastructure) and Phase 3 (Reporting) Public APIs Summary

This document provides a comprehensive guide to all public APIs available from Phase 1 and Phase 3 that Phase 4 Orchestration will need to use.

## Phase 1: Infrastructure Services

Located at: `/workspace/performance-tester-dotnet/src/PerformanceTester.Infrastructure`

### Dependency Injection Setup

```csharp
// In Program.cs or Host builder:
builder.Services.AddInfrastructure(
    postgresConnectionString: string,
    rabbitMqConnectionString: string,
    rabbitMqManagementPort?: int  // Optional, inferred if not provided
);
```

### 1. IServiceDiscovery Interface

**Namespace:** `PerformanceTester.Infrastructure`

**Purpose:** Discovers processes listening on network ports (cross-platform: Linux/Windows)

**Method Signature:**
```csharp
public interface IServiceDiscovery
{
    /// <summary>Finds the process ID listening on the specified port</summary>
    /// <param name="port">Port number (1-65535)</param>
    /// <param name="timeout">Maximum time to wait for service to appear</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Process ID if found, null if not found within timeout</returns>
    /// <exception cref="ArgumentOutOfRangeException">Port not in valid range</exception>
    /// <exception cref="OperationCanceledException">Operation was cancelled</exception>
    Task<int?> FindServiceProcessIdAsync(
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
```

**Usage Example:**
```csharp
var serviceDiscovery = host.Services.GetRequiredService<IServiceDiscovery>();
var processId = await serviceDiscovery.FindServiceProcessIdAsync(
    port: 8094,
    timeout: TimeSpan.FromSeconds(30)
);
```

**Implementation Details:**
- Uses Linux/Windows platform-specific implementations injected at DI registration time
- Polls for port listening with 1-second intervals
- Logs progress every 5 seconds
- No runtime platform checks (#if directives)

### 2. IDatabase Interface

**Namespace:** `PerformanceTester.Infrastructure`

**Purpose:** Manages PostgreSQL databases during testing (dynamic table discovery + truncation)

**Method Signature:**
```csharp
public interface IDatabase
{
    /// <summary>Clears all data from specified database by truncating all tables</summary>
    /// <param name="databaseName">Name of the database to clear</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="ArgumentException">Database name is null or empty</exception>
    /// <exception cref="InvalidOperationException">Database clearing failed after retries</exception>
    Task ClearDatabaseAsync(
        string databaseName,
        CancellationToken cancellationToken = default);
}
```

**Usage Example:**
```csharp
var database = host.Services.GetRequiredService<IDatabase>();
await database.ClearDatabaseAsync("dotnet9_db");
```

**Implementation Details:**
- Dynamically discovers all tables in public schema (no hardcoded table names)
- Uses TRUNCATE TABLE ... CASCADE to clear data
- Auto-retry logic: 2 attempts with 2-second delay between retries
- Verifies cleanup with row count check
- Logs detailed progress with visual symbols (✓, ⚠️)

### 3. IRabbitMQ Interface

**Namespace:** `PerformanceTester.Infrastructure`

**Purpose:** Manages RabbitMQ queues during testing (discovery + purging)

**Method Signature:**
```csharp
public interface IRabbitMQ
{
    /// <summary>Purges all messages from all queues in the default vhost</summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="InvalidOperationException">Queue clearing failed</exception>
    Task ClearAllQueuesAsync(CancellationToken cancellationToken = default);
}
```

**Usage Example:**
```csharp
var rabbitMq = host.Services.GetRequiredService<IRabbitMQ>();
await rabbitMq.ClearAllQueuesAsync();
```

**Implementation Details:**
- Uses RabbitMQ Management HTTP API (not AMQP)
- Dynamically discovers all queues (no hardcoded queue names)
- Purges each queue individually
- Continues on individual queue failures
- Infers Management API port from AMQP port:
  - Standard: AMQP 5672 → Management 15672
  - Testcontainers: AMQP 20001 → Management 20002
  - Custom: Explicitly provided via DI parameter
- Basic authentication from AMQP connection string

---

## Phase 3: Reporting Services

Located at: `/workspace/performance-tester-dotnet/src/PerformanceTester.Reporting`

### Dependency Injection Setup

```csharp
// In Program.cs or Host builder:
builder.Services.AddReporting();
```

Platform detection (Windows/Linux/WSL2) happens automatically at registration time.

### 1. ISystemInfoDetector Interface

**Namespace:** `PerformanceTester.Reporting`

**Purpose:** Detects hardware and OS information (CPU, RAM, disks, OS version, WSL version)

**Method Signature:**
```csharp
public interface ISystemInfoDetector
{
    /// <summary>Detects complete system information (CPU, RAM, disks, OS)</summary>
    /// <remarks>Result is cached for performance (expensive operation)</remarks>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>System information, or null if detection fails</returns>
    Task<SystemInfo?> GetSystemInfoAsync(CancellationToken cancellationToken = default);
}
```

**Usage Example:**
```csharp
var systemInfoDetector = host.Services.GetRequiredService<ISystemInfoDetector>();
var systemInfo = await systemInfoDetector.GetSystemInfoAsync();
```

**Implementation Details:**
- Uses caching: Result is lazily evaluated once and cached
- Platform-specific implementations:
  - **Linux:** Parses /proc/cpuinfo, /proc/meminfo, uses lsblk command, detects WSL2 via /proc/version
  - **Windows:** Uses WMI queries
  - **WSL2:** Queries Windows host via PowerShell for accurate RAM/disk info
- 10-second timeout for external commands (WSL2 PowerShell queries)
- Filter: Only includes disks >= 500GB (matches Python behavior)

### 2. IReportGenerator Interface

**Namespace:** `PerformanceTester.Reporting`

**Purpose:** Generates JSON test reports from performance test data (7 separate JSON files)

**Method Signature:**
```csharp
public interface IReportGenerator
{
    /// <summary>Generates all report files (JSON, resource metrics, throughput metrics)</summary>
    /// <param name="outputDirectory">Directory to write report files</param>
    /// <param name="testReport">Complete test report data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task GenerateReportAsync(
        string outputDirectory,
        TestReport testReport,
        CancellationToken cancellationToken = default);
}
```

**Usage Example:**
```csharp
var reportGenerator = host.Services.GetRequiredService<IReportGenerator>();
await reportGenerator.GenerateReportAsync(
    outputDirectory: "/path/to/reports",
    testReport: testReport
);
```

**Generated Files (7 total):**
1. `test-report-{timestamp}-{servicename}.json` - Main report
2. `test-report-{timestamp}-{servicename}.events-throughput.json` - Event throughput metrics
3. `test-report-{timestamp}-{servicename}.api-throughput.json` - API throughput metrics
4. `test-report-{timestamp}-{servicename}.resource-metrics.json` - Process resource metrics
5. `test-report-{timestamp}-{servicename}.system-metrics.json` - System-wide metrics
6. `test-report-{timestamp}-{servicename}.rabbitmq-metrics.json` - RabbitMQ container metrics
7. `test-report-{timestamp}-{servicename}.postgres-metrics.json` - PostgreSQL container metrics

**Implementation Details:**
- Uses snake_case JSON naming convention
- Creates output directory if not exists
- Main report timestamp format: "yyyy-MM-dd HH:mm:ss"
- Sample timestamps: ISO8601 with +00:00 timezone
- Processes service names for filename sanitization
- Generates statistical summaries (avg, min, max, std dev, CV%)

### 3. IChartGenerator Interface

**Namespace:** `PerformanceTester.Reporting`

**Purpose:** Generates PNG charts with 5-subplot visualizations (ScottPlot-based)

**Method Signature:**
```csharp
public interface IChartGenerator
{
    /// <summary>Generates a PNG chart with 5 subplots showing all performance metrics</summary>
    /// <param name="outputPath">Full path to output PNG file</param>
    /// <param name="testReport">Complete test report data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task GenerateChartAsync(
        string outputPath,
        TestReport testReport,
        CancellationToken cancellationToken = default);
}
```

**Usage Example:**
```csharp
var chartGenerator = host.Services.GetRequiredService<IChartGenerator>();
await chartGenerator.GenerateChartAsync(
    outputPath: "/path/to/chart.png",
    testReport: testReport
);
```

**Chart Structure (5 Subplots):**
1. **Throughput:** Events/sec + API calls/sec (dual line with fills and averages)
2. **Service CPU/RAM:** Dual Y-axes with trend lines
3. **RabbitMQ CPU/RAM:** Dual Y-axes with trend lines
4. **PostgreSQL CPU/RAM:** Dual Y-axes with trend lines
5. **System CPU/RAM:** Dual Y-axes with trend lines

**Chart Features:**
- Dimensions: 2100px width × 2400px total height (480px per subplot)
- Phase boundaries marked with vertical lines (consume/API phases)
- Phase labels on top subplot
- Legend with statistics (avg, min, max, mode, std dev, CV%)
- Color-coded by component (Events, API, Service, RabbitMQ, PostgreSQL, System)
- Grid enabled for easy reading
- X-axis synchronized across all subplots
- Only bottom plot shows X-axis label ("Time")
- Title shows service name, date, and time

**Implementation Details:**
- Loads data from 7 JSON files generated by ReportGenerator
- Uses ScottPlot 5.0 with SkiaSharp for rendering
- Requires all metric data files to be present
- Creates output directory if not exists

### 4. IComparisonReportGenerator Interface

**Namespace:** `PerformanceTester.Reporting`

**Purpose:** Generates Markdown comparison reports across multiple test runs with rankings/medals

**Method Signature:**
```csharp
public interface IComparisonReportGenerator
{
    /// <summary>Generates a Markdown comparison report from multiple test reports</summary>
    /// <param name="outputPath">Full path to output Markdown file</param>
    /// <param name="testReports">Collection of test reports to compare</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task GenerateComparisonReportAsync(
        string outputPath,
        IEnumerable<TestReport> testReports,
        CancellationToken cancellationToken = default);
}
```

**Usage Example:**
```csharp
var comparisonGen = host.Services.GetRequiredService<IComparisonReportGenerator>();
await comparisonGen.GenerateComparisonReportAsync(
    outputPath: "/path/to/comparison.md",
    testReports: testReports
);
```

**Report Sections:**
1. **Header** - Generated timestamp
2. **Test Environment** - CPU, RAM, disks, OS, platform, test run times
3. **Test Runs Overview** - Runtime comparison (sorted, lower is better)
4. **Throughput Comparison:**
   - Event processing throughput
   - API throughput
5. **Resource Usage Comparison:**
   - Process CPU usage
   - Process memory usage
   - System-wide metrics
6. **Performance Highlights** - Best performers by category

**Table Features:**
- Medal awards: 🥇 (1st), 🥈 (2nd), 🥉 (3rd) per column
- Service names sanitized for display
- Proper column alignment and formatting
- Footnotes explaining metrics (Min excludes zeros, Std Dev, CV%)
- Support for WSL2 detection (shows "physical drive(s) on Windows host")

---

## Data Models

### TestReport (Main Data Structure)

**Namespace:** `PerformanceTester.Reporting`

**Purpose:** Aggregates all test data collected during a performance test run

```csharp
public sealed record TestReport
{
    public required DateTime TestDate { get; init; }
    public required double TotalRuntimeSeconds { get; init; }
    public required PhaseTimestamps PhaseTimestamps { get; init; }
    public required MonitoredProcess MonitoredProcess { get; init; }
    public required SystemInfo System { get; init; }
    public required TestConfiguration Configuration { get; init; }
    public required TestResults Results { get; init; }

    // Sample collections (from Phase 2 monitoring, 100ms sampling interval):
    public IReadOnlyList<ThroughputSample> EventsThroughputSamples { get; init; }
    public IReadOnlyList<ThroughputSample> ApiThroughputSamples { get; init; }

    // Process metrics (500ms sampling interval):
    public IReadOnlyList<ProcessResourceSample> ProcessResourceSamples { get; init; }

    // Container metrics (500ms for system, 3000ms for containers):
    public IReadOnlyList<ContainerResourceSample> SystemResourceSamples { get; init; }
    public IReadOnlyList<ContainerResourceSample> RabbitMqResourceSamples { get; init; }
    public IReadOnlyList<ContainerResourceSample> PostgresResourceSamples { get; init; }
}
```

**Nested Record: PhaseTimestamps**
```csharp
public sealed record PhaseTimestamps
{
    public required double Phase1Start { get; init; }  // Always 0.0
    public required double Phase1End { get; init; }
    public required double Phase2Start { get; init; }
    public required double Phase2End { get; init; }
    public required double Phase3Start { get; init; }
    public required double Phase3End { get; init; }
}
```

**Nested Record: MonitoredProcess**
```csharp
public sealed record MonitoredProcess
{
    public required string Name { get; init; }  // e.g., "goReferenceService"
    public required int Pid { get; init; }
}
```

**Nested Record: TestConfiguration**
```csharp
public sealed record TestConfiguration
{
    public required int NumEvents { get; init; }
    public required string ApiDuration { get; init; }  // e.g., "30s", "2m"
    public required int ApiConcurrentWorkers { get; init; }
    public required string RabbitmqExchange { get; init; }
    public required string ConsumerQueue { get; init; }
    public required string ApiEndpoint { get; init; }
    public required string PublishEventType { get; init; }
    public required string ConsumeEventType { get; init; }
}
```

**Nested Record: TestResults**
```csharp
public sealed record TestResults
{
    public required PublishResults Phase1Publish { get; init; }
    public required ConsumeResults Phase2Consume { get; init; }
    public required ApiResults Phase3Api { get; init; }
}
```

### SystemInfo Model

**Namespace:** `PerformanceTester.Reporting`

**Purpose:** Hardware and OS information for the test environment

```csharp
public sealed record SystemInfo
{
    public required string Os { get; init; }
    public required string OsRelease { get; init; }
    public required string OsVersion { get; init; }
    public string? WslVersion { get; init; }  // "WSL1", "WSL2", or null
    public required CpuInfo Cpu { get; init; }
    public required RamInfo Ram { get; init; }
    public required IReadOnlyList<DiskInfo> Disks { get; init; }
}

public sealed record CpuInfo
{
    public required string Model { get; init; }
    public required int LogicalProcessors { get; init; }
    public required int PhysicalProcessors { get; init; }
    public double? SpeedMhz { get; init; }
}

public sealed record RamInfo
{
    public required double TotalGb { get; init; }
    public string? Speed { get; init; }
    public string? Type { get; init; }  // e.g., "DDR4", "DDR5"
    public string? Manufacturer { get; init; }
}

public sealed record DiskInfo
{
    public required string Name { get; init; }
    public required string Size { get; init; }  // e.g., "2.0T", "500.0G"
    public required string Type { get; init; }
    public string? Model { get; init; }
}
```

### Sample Models

**ThroughputSample (100ms sampling interval)**
```csharp
public sealed record ThroughputSample
{
    public required DateTime Timestamp { get; init; }
    public required double ElapsedSeconds { get; init; }
    public required double Rate { get; init; }  // events/s or calls/s
    public required int CumulativeCount { get; init; }
}
```

**ProcessResourceSample (500ms sampling interval)**
```csharp
public sealed record ProcessResourceSample
{
    public required DateTime Timestamp { get; init; }
    public required double ElapsedSeconds { get; init; }
    public required double CpuPercent { get; init; }
    public required double MemoryRssMb { get; init; }  // Resident Set Size
    public required int Threads { get; init; }
}
```

**ContainerResourceSample (500ms for system, 3000ms for containers)**
```csharp
public sealed record ContainerResourceSample
{
    public required DateTime Timestamp { get; init; }
    public required double ElapsedSeconds { get; init; }
    public required double CpuPercent { get; init; }
    public required double MemoryMb { get; init; }
}
```

---

## Configuration & Environment Variables

Both Phase 1 and Phase 3 use environment variables for configuration:

### Phase 1 (Infrastructure)

**PostgreSQL Connection:**
```
POSTGRES_HOST=localhost
POSTGRES_PORT=5432
POSTGRES_DB=postgres
POSTGRES_USER=admin
POSTGRES_PASSWORD=admin
```

**RabbitMQ Connection:**
```
RABBITMQ_HOST=localhost
RABBITMQ_PORT=5672
RABBITMQ_USER=admin
RABBITMQ_PASS=admin
RABBITMQ_MANAGEMENT_PORT=15672  # Optional, inferred if not provided
```

---

## Exception Handling

### IServiceDiscovery
- `ArgumentOutOfRangeException` - Port not in range 1-65535
- `OperationCanceledException` - Operation was cancelled

### IDatabase
- `ArgumentException` - Database name is null or empty
- `InvalidOperationException` - Database clearing failed after retries

### IRabbitMQ
- `InvalidOperationException` - Queue clearing failed

### ISystemInfoDetector
- Returns `null` if detection fails (graceful degradation)

### IReportGenerator
- `ArgumentException` - Output directory null/empty
- `ArgumentNullException` - TestReport is null

### IChartGenerator
- `ArgumentException` - Output path null/empty
- `ArgumentNullException` - TestReport is null
- `InvalidOperationException` - No metrics data files found

### IComparisonReportGenerator
- `ArgumentException` - Output path or test reports empty
- `ArgumentNullException` - Test reports collection is null

---

## Design Patterns Used

### 1. Strategy Pattern (OS Detection)
- Single platform detection at DI registration time
- Platform-specific implementations (Linux, Windows, WSL2)
- No runtime `#if` directives in service code
- Enables easy addition of new platforms (macOS, etc.)

### 2. Dependency Injection
- All services registered as singletons
- Clean constructor injection
- No service locator antipattern

### 3. Async/Await
- All I/O operations are async
- Supports cancellation tokens throughout
- No blocking calls

### 4. Records (C# 9+)
- Immutable data models
- Built-in value equality
- No need for manual GetHashCode/Equals

### 5. Vertical Slice Architecture
- Infrastructure and Reporting are self-contained slices
- Each owns its models and interfaces
- Minimal coupling to other phases

---

## Key Differences from Python Implementation

### Infrastructure
- **.NET vs Python:** Uses native .NET APIs instead of shell scripts
- **Platform Detection:** Happens once at DI registration, not per method call
- **Connection Management:** Uses Npgsql connection pooling and RabbitMQ HTTP API

### Reporting
- **.NET Charts:** ScottPlot-based PNG generation instead of Matplotlib
- **Statistical Calculations:** Uses MathNet.Numerics instead of NumPy
- **System Detection:** WMI on Windows, /proc parsing on Linux, PowerShell for WSL2
- **Caching:** SystemInfo detection result is cached (Lazy<T>)

---

## Performance Considerations

- **IServiceDiscovery:** Polls every 1 second, acceptable overhead for test setup
- **IDatabase:** Automatic retry with 2-second delays handles transient failures
- **IRabbitMQ:** Uses HTTP API for discovery (more reliable than AMQP-only approach)
- **ISystemInfoDetector:** Result cached after first call (expensive operation)
- **IChartGenerator:** Loads JSON files incrementally, doesn't load unused data

---

## Integration Requirements for Phase 4

Phase 4 Orchestration will need to:

1. **Register Services** at application startup:
   ```csharp
   builder.Services.AddInfrastructure(postgresConnStr, rabbitMqConnStr);
   builder.Services.AddReporting();
   ```

2. **Cleanup Before Each Test:**
   ```csharp
   var database = host.Services.GetRequiredService<IDatabase>();
   var rabbitMq = host.Services.GetRequiredService<IRabbitMQ>();
   
   await database.ClearDatabaseAsync("service_db");
   await rabbitMq.ClearAllQueuesAsync();
   ```

3. **Discover Service After Launch:**
   ```csharp
   var discovery = host.Services.GetRequiredService<IServiceDiscovery>();
   var pid = await discovery.FindServiceProcessIdAsync(port, TimeSpan.FromSeconds(30));
   ```

4. **Generate Reports:**
   ```csharp
   var reportGen = host.Services.GetRequiredService<IReportGenerator>();
   await reportGen.GenerateReportAsync(outputDir, testReport);
   
   var chartGen = host.Services.GetRequiredService<IChartGenerator>();
   await chartGen.GenerateChartAsync(chartPath, testReport);
   ```

5. **Generate Comparison Reports:**
   ```csharp
   var comparisonGen = host.Services.GetRequiredService<IComparisonReportGenerator>();
   await comparisonGen.GenerateComparisonReportAsync(outputPath, allTestReports);
   ```

All operations support `CancellationToken` for proper cleanup and timeout handling.
