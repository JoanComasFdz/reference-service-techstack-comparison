# Performance Tester .NET Project Exploration

## Executive Summary

**Project:** PerformanceTester.sln - Comprehensive .NET 9 testing and infrastructure tools for cross-language microservice performance comparison

**Architecture:** Vertical Slice Architecture (VSA) with 25 projects organized into 5 phases
- Phase 0: Integration Testing Foundation
- Phase 1: Infrastructure (Service Discovery, Database, RabbitMQ)
- Phase 2: Data Collection (Event Publishing/Consuming, Process/Docker Monitoring, API Load Testing)
- Phase 3: Reporting (JSON reports, PNG charts, comparisons)
- Phase 4: Orchestration (Complete test workflow coordination)
- Phase 5: CLI (Command-line interface) - IN PROGRESS

**Current Focus:** CLI project needs to wrap primitives in value objects using Result pattern

---

## 1. Solution Structure & Projects

### Core Project Dependencies
```
25 Projects Total:
├── Foundation
│   ├── PerformanceTester.Common (utilities, OS detection)
│   ├── PerformanceTester.IntegrationTesting (Testcontainers lifecycle)
│   └── JoanComasFdz.Result (discriminated union library)
├── Phase 1: Infrastructure (6 projects)
│   ├── PerformanceTester.Infrastructure (main)
│   └── *.IntegrationTests (5 test projects)
├── Phase 2: Data Collection (12 projects)
│   ├── EventPublishing (owns CloudEvent model)
│   ├── EventConsuming (owns ThroughputSample)
│   ├── ProcessMonitoring (owns ProcessMetrics)
│   ├── DockerMonitoring (owns DockerMetrics)
│   ├── ApiLoadTesting (owns K6Result)
│   └── 6+ test projects
├── Phase 3: Reporting (2 projects)
│   ├── PerformanceTester.Reporting
│   └── *.IntegrationTests
├── Phase 4: Orchestration (2 projects)
│   ├── PerformanceTester.Orchestration
│   └── *.IntegrationTests
└── Phase 5: CLI (2 projects)
    ├── PerformanceTester.Cli (ACTIVE)
    └── PerformanceTester.Cli.Tests
```

### Key Observation
- All projects use **Vertical Slice Architecture** (not layered)
- Each slice "owns" its models (no central Core project)
- Producers expose interfaces, consumers depend on them
- Zero project-to-project dependencies (except for tests)

---

## 2. CLI Project Structure (Primary Focus)

### Location
`/workspace/performance-tester-dotnet/src/PerformanceTester.Cli`

### Key Files

#### Program.cs
- Entry point using System.CommandLine
- Sets up Serilog with ProgressAwareConsoleSink
- DI Configuration via Host.CreateDefaultBuilder()
- Two commands: TestCommand and CompareCommand

#### TestCommand.cs
- **12 CLI Options** (all using primitive types):
  - `--events, -e` (int, default 10000)
  - `--api-duration, -d` (string, default "30s")
  - `--api-workers, -w` (int, default 1)
  - `--port, -p` (int, default 8080)
  - `--database, -b` (string, default "defaultdb")
  - `--results-folder, -r` (string, default "./test-results")
  - `--warmup-events` (int, default 200)
  - `--warmup-api-calls` (int, default 10)
  - `--inactivity-timeout` (string, default "120s")
  - `--rabbitmq-container` (string)
  - `--postgres-container` (string)

- **Data Flow:**
  1. System.CommandLine parses raw CLI args → primitives
  2. Primitives passed to `TestCommandOptions` record
  3. `ValidateOptions()` checks ranges/formats (returns string? error)
  4. `DurationParser.Parse()` converts string → Result<TimeSpan, DurationParseError>
  5. Primitives wrapped in `TestConfiguration` record
  6. Configuration passed to ITestOrchestrator

- **Validation Pattern:**
  ```csharp
  static string? ValidateOptions(TestCommandOptions options)
  {
      if (options.Events < 1 || options.Events > 1_000_000)
          return $"Events must be between 1 and 1,000,000";
      // ... 11 more validations
      return null;
  }
  ```

#### CompareCommand.cs
- 3 CLI Options (all primitives):
  - `--folder, -f` (string, default "./test-results")
  - `--output, -o` (string?, optional)
  - `--stdout` (bool, default false)
- Loads JSON files with custom sample types
- Generates comparison reports

#### Configuration Files

**AppConfiguration.cs**
```csharp
public sealed class AppConfiguration
{
    // Environment variable loading
    public string PostgresConnectionString { get; init; }
    public string RabbitMqConnectionString { get; init; }
    public string RabbitMqContainerName { get; init; }
    public string PostgresContainerName { get; init; }

    public static AppConfiguration Load(IConfiguration configuration)
    {
        // Loads from POSTGRES_HOST, POSTGRES_PORT, POSTGRES_DB, etc.
        // Returns fully constructed object
    }
}
```

**DurationParser.cs** (Already using Result pattern!)
```csharp
using static JoanComasFdz.Result.Result<System.TimeSpan, DurationParseError>;

public static Result<TimeSpan, DurationParseError> Parse(string duration)
{
    if (string.IsNullOrWhiteSpace(duration))
        return new Failure(new DurationParseError.Empty());
    
    var match = DurationPattern().Match(duration);
    if (!match.Success)
        return new Failure(new DurationParseError.InvalidFormat(duration));
    
    // Parse and return Success or Failure
}

public static bool IsValid(string duration) =>
    Parse(duration).Match(
        success: _ => true,
        failure: _ => false);
```

**DurationParseError.cs** (Using Dunet union types)
```csharp
[Union]
public partial record DurationParseError
{
    public partial record Empty;
    public partial record InvalidFormat(string Input);
    public partial record UnknownUnit(char Unit);
}
```

---

## 3. Configuration & Options Patterns

### Current Pattern: Primitives in Records
```csharp
public sealed record TestCommandOptions(
    int Events,
    string ApiDuration,
    int ApiWorkers,
    int Port,
    string Database,
    string ResultsFolder,
    int WarmupEvents,
    int WarmupApiCalls,
    string InactivityTimeout,
    string RabbitMqContainer,
    string PostgresContainer);

public sealed record CompareCommandOptions(
    string Folder,
    string? Output,
    bool Stdout);
```

### Key Issue
**Primitives are validated AFTER creation:**
1. CLI args → Records with raw values
2. ValidateOptions() checks ranges/formats
3. If validation fails, return error string

**Better Pattern (Result-based):**
1. CLI args → Value Objects (already validated)
2. Pass to commands with confidence
3. Type system enforces valid state

---

## 4. Orchestration & Configuration Usage

### TestConfiguration Record
Location: `PerformanceTester.Orchestration/TestConfiguration.cs`

```csharp
public record TestConfiguration(
    int EventCount = 10000,                                  // Raw primitive
    TimeSpan? ApiDuration = null,                           // Already TimeSpan
    int ApiWorkers = 1,                                      // Raw primitive
    TimeSpan? InactivityTimeout = null,                     // Already TimeSpan
    int WarmupEventCount = 200,                             // Raw primitive
    uint WarmupApiCallCount = 10,                           // Raw primitive (uint!)
    TimeSpan? WarmupInactivityTimeout = null,               // Already TimeSpan
    int ServicePort = 8080,                                 // Raw primitive
    string DatabaseName = "defaultdb",                      // Raw primitive
    string ResultsFolder = "./test-results",                // Raw primitive
    string RabbitMqContainerName = "...",                  // Raw primitive
    string PostgresContainerName = "...",                  // Raw primitive
    int MaxConsecutiveApiFailures = 3)                     // Raw primitive
{
    // Helper properties for defaults
    public TimeSpan ApiDurationOrDefault => ApiDuration ?? TimeSpan.FromSeconds(30);
    public TimeSpan InactivityTimeoutOrDefault => InactivityTimeout ?? TimeSpan.FromSeconds(120);
    public TimeSpan WarmupInactivityTimeoutOrDefault => WarmupInactivityTimeout ?? TimeSpan.FromSeconds(30);
    public string ApiUrl => $"http://localhost:{ServicePort}/kpi";
}
```

**Current Data Flow:**
```
TestCommand.ExecuteAsync()
  → ValidateOptions(options: TestCommandOptions)
  → DurationParser.Parse(options.ApiDuration)  // Result<TimeSpan, DurationParseError>
  → new TestConfiguration(
      EventCount: options.Events,
      ApiDuration: parsedDuration.Value,
      ...
  )
  → ITestOrchestrator.RunTestAsync(config)
```

---

## 5. Result Pattern Usage

### JoanComasFdz.Result Library
Location: `/workspace/performance-tester-dotnet/src/JoanComasFdz.Result/`

```csharp
[Union]
public partial record Result<TSuccess, TFailure>
{
    partial record Success(TSuccess Value);
    partial record Failure(TFailure Error);
}
```

**Uses Dunet library** for discriminated unions with exhaustive pattern matching.

### Current Result Usage in Project

**1. DurationParser** (Already using Result)
```csharp
using static Result<TimeSpan, DurationParseError>;

public static Result<TimeSpan, DurationParseError> Parse(string duration)
{
    // Returns Success or Failure
}

// Usage in TestCommand
var apiDuration = ((Result<TimeSpan, DurationParseError>.Success)
    DurationParser.Parse(options.ApiDuration)).Value;
```

**2. ServiceDiscovery** (Using Result)
```csharp
using static Result<int, string>;

public async Task<Result<int, string>> FindServiceProcessIdAsync(int port, TimeSpan timeout, ...)
{
    if (port < 1 || port > 65535)
        throw new ArgumentOutOfRangeException(nameof(port), ...);
    
    // ... logic ...
    
    return new Success(processId);  // or new Failure(message)
}
```

**3. DatabaseCleaner** (Using Result)
```csharp
public async Task<Result<Unit, ClearDatabaseError>> ClearDatabaseAsync(...)
```

**4. RabbitMqCleaner** (Using Result)
```csharp
public async Task<Result<Unit, string>> ClearAllQueuesAsync(...)
```

**5. K6MetricsParser** (Using Result)
```csharp
// Parses k6 output lines and returns Result
```

### Recent Git History
```
3949025 refactor(result): simplify Result usage in Divide method and update examples
aa8e2f8 refactor(cli): remove unused DurationParser.TryParse method
16d774c refactor(result): replace native pattern matching with dunet Match
2719246 refactor(result): use 'using static' to shorten Result construction
c2e7b01 refactor(result): unify Result types into single Result<TSuccess, TFailure>
2130eb3 refactor(infrastructure): migrate ServiceDiscovery, DatabaseCleaner, RabbitMqCleaner
2e9b33b refactor(cli): migrate DurationParser.Parse to Result pattern
c16758a refactor(api-load-testing): migrate K6MetricsParser.ParseLine to Result pattern
fc7d2eb feat(result): add JoanComasFdz.Result library
```

---

## 6. Method Signatures & Primitive Usage Patterns

### CLI Command Options (Raw Primitives)
```csharp
// Option declarations use raw types
new Option<int>("--events", getDefaultValue: () => 10000, ...)
new Option<string>("--api-duration", getDefaultValue: () => "30s", ...)
new Option<int>("--api-workers", getDefaultValue: () => 1, ...)
new Option<int>("--port", getDefaultValue: () => 8080, ...)
new Option<string>("--database", getDefaultValue: () => "defaultdb", ...)
new Option<string>("--results-folder", getDefaultValue: () => "./test-results", ...)
new Option<int>("--warmup-events", getDefaultValue: () => 200, ...)
new Option<int>("--warmup-api-calls", getDefaultValue: () => 10, ...)
new Option<string>("--inactivity-timeout", getDefaultValue: () => "120s", ...)
new Option<string>("--rabbitmq-container", getDefaultValue: () => "performancetest-rabbitmq", ...)
new Option<string>("--postgres-container", getDefaultValue: () => "performancetest-postgres", ...)
```

### Infrastructure Methods (Mixed Types)
```csharp
// Infrastructure uses Result pattern for errors
public async Task<Result<int, string>> FindServiceProcessIdAsync(
    int port,                           // Raw int - NOT wrapped
    TimeSpan timeout,                   // Already value type
    CancellationToken cancellationToken = default)

public async Task<Result<Unit, ClearDatabaseError>> ClearDatabaseAsync(
    string databaseName,               // Raw string - NOT wrapped
    CancellationToken cancellationToken = default)

public async Task<PublishMetrics> PublishEventsAsync(
    int count,                         // Raw int - NOT wrapped
    CancellationToken cancellationToken = default)
```

### Event Publishing
```csharp
public async Task<PublishMetrics> PublishEventsAsync(int count, CancellationToken cancellationToken = default)
{
    // EventCount validated inside method (0 check)
    // Returns PublishMetrics record with aggregated data
}
```

### Validation Pattern - Explicit Check Then Use
```csharp
// Before parsing
if (options.Events < 1 || options.Events > 1_000_000)
    return $"Events must be between 1 and 1,000,000 (got: {options.Events})";

if (options.ApiWorkers < 1 || options.ApiWorkers > 1000)
    return $"API workers must be between 1 and 1000 (got: {options.ApiWorkers})";

if (options.Port < 1 || options.Port > 65535)
    return $"Port must be between 1 and 65535 (got: {options.Port})";

// Then parse duration (which returns Result)
if (!DurationParser.IsValid(options.ApiDuration))
    return $"Invalid API duration format: '{options.ApiDuration}'";

// After validation passes, unwrap Result
var apiDuration = ((Result<TimeSpan, DurationParseError>.Success)
    DurationParser.Parse(options.ApiDuration)).Value;
```

---

## 7. Value Objects & Domain Types in Use

### TimeSpan (Already a Value Type)
- ApiDuration: TimeSpan
- InactivityTimeout: TimeSpan
- TestConfiguration already uses TimeSpan

### Port (Currently int)
```csharp
// ServicePort is int in TestConfiguration
public int ServicePort = 8080;

// Used in TestConfiguration
public string ApiUrl => $"http://localhost:{ServicePort}/kpi";
```

### EventCount (Currently int)
```csharp
// EventCount is int in TestConfiguration
public int EventCount = 10000;

// Used in orchestration
var metrics = await publisher.PublishEventsAsync(config.EventCount);
```

### DatabaseName (Currently string)
```csharp
// DatabaseName is string in TestConfiguration
public string DatabaseName = "defaultdb";

// Used in orchestration
var result = await _database.ClearDatabaseAsync(config.DatabaseName);
```

### ContainerName (Currently string)
```csharp
// RabbitMqContainerName is string in TestConfiguration
public string RabbitMqContainerName = "performancetest-rabbitmq";
public string PostgresContainerName = "performancetest-postgres";
```

### FolderPath (Currently string)
```csharp
// ResultsFolder is string
public string ResultsFolder = "./test-results";

// Used to create directories
Directory.CreateDirectory(config.ResultsFolder);
```

### Connection Strings (Currently string)
```csharp
// AppConfiguration has raw strings
public string PostgresConnectionString { get; init; }
public string RabbitMqConnectionString { get; init; }

// Passed to DI
services.AddOrchestration(
    postgresConnectionString: appConfig.PostgresConnectionString,
    rabbitMqConnectionString: appConfig.RabbitMqConnectionString,
    ...
);
```

### Sample Collections (Already domain types)
```csharp
// Reporting uses domain types, not primitives
public IReadOnlyList<ThroughputMetricSample> EventsThroughputSamples { get; init; }
public IReadOnlyList<ProcessResourceSample> ProcessResourceSamples { get; init; }
public IReadOnlyList<SystemResourceSample> SystemResourceSamples { get; init; }
public IReadOnlyList<ContainerResourceSample> RabbitMqResourceSamples { get; init; }
```

---

## 8. Configuration Loading Pattern

### Current Pattern (AppConfiguration.cs)
```csharp
public static AppConfiguration Load(IConfiguration configuration)
{
    // Environment variables or defaults
    var pgHost = configuration["POSTGRES_HOST"] ?? "localhost";
    var pgPort = configuration["POSTGRES_PORT"] ?? "5432";
    var pgUser = configuration["POSTGRES_USER"] ?? "admin";
    var pgPassword = configuration["POSTGRES_PASSWORD"] ?? "admin";
    var pgDatabase = configuration["POSTGRES_DB"] ?? "postgres";

    var rmqHost = configuration["RABBITMQ_HOST"] ?? "localhost";
    var rmqPort = configuration["RABBITMQ_PORT"] ?? "5672";
    var rmqUser = configuration["RABBITMQ_USER"] ?? "admin";
    var rmqPassword = configuration["RABBITMQ_PASS"] ?? "admin";

    var rmqContainer = configuration["RABBITMQ_CONTAINER"] ?? "performancetest-rabbitmq";
    var pgContainer = configuration["POSTGRES_CONTAINER"] ?? "performancetest-postgres";

    return new AppConfiguration
    {
        PostgresConnectionString = $"Host={pgHost};Port={pgPort};...",
        RabbitMqConnectionString = $"amqp://{rmqUser}:{rmqPassword}@{rmqHost}:{rmqPort}",
        RabbitMqContainerName = rmqContainer,
        PostgresContainerName = pgContainer
    };
}
```

**Issues:**
1. Port read as string, used as string in connection string (no type safety)
2. No validation that port is valid
3. No validation that host is reachable
4. Raw strings assembled into connection strings without protection

---

## 9. Recent Refactoring (Result Pattern Migration)

### Latest Commits Show Direction
1. **c2e7b01** - Unified Result types into single `Result<TSuccess, TFailure>`
2. **2719246** - Use 'using static' to shorten Result construction
3. **16d774c** - Replace native pattern matching with dunet Match
4. **3949025** - Simplify Result usage in examples

### Pattern Being Established
```csharp
// Always: using static Result<TSuccess, TFailure>;
using static JoanComasFdz.Result.Result<System.TimeSpan, DurationParseError>;

// Return Success/Failure directly (no 'new Result<...>')
return new Success(value);
return new Failure(error);

// Use Dunet's Match for pattern matching
result.Match(
    success: s => Console.WriteLine($"Got {s.Value}"),
    failure: f => f.Error.Match(
        empty: _ => "Empty",
        invalidFormat: err => $"Invalid: {err.Input}",
        unknownUnit: err => $"Unknown unit: {err.Unit}"
    )
);
```

---

## 10. Opportunities for Value Objects

### 1. Port Number (int → Port)
- Range: 1-65535
- Used in:
  - TestConfiguration.ServicePort
  - CLI option
  - Service discovery
  - Connection strings

### 2. Event Count (int → EventCount)
- Range: 1-1,000,000
- Used in:
  - TestConfiguration.EventCount
  - CLI option
  - Event publishing metrics

### 3. Concurrent Workers (int → WorkerCount)
- Range: 1-1000
- Used in:
  - TestConfiguration.ApiWorkers
  - CLI option
  - k6 load tester

### 4. Database Name (string → DatabaseName)
- Not empty, not whitespace
- Matches pattern: `^[a-zA-Z_][a-zA-Z0-9_]*$`
- Used in:
  - TestConfiguration.DatabaseName
  - CLI option
  - Database operations

### 5. Folder Path (string → FolderPath)
- Valid file system path
- Optional: auto-create if missing
- Used in:
  - TestConfiguration.ResultsFolder
  - CLI option
  - Directory operations

### 6. Container Name (string → ContainerName)
- Not empty, not whitespace
- Used in:
  - TestConfiguration.RabbitMqContainerName
  - TestConfiguration.PostgresContainerName
  - CLI options

### 7. Duration (string → Duration)
- Already has DurationParser with Result pattern
- Used in:
  - TestConfiguration.ApiDuration
  - TestConfiguration.InactivityTimeout
  - CLI options

### 8. Host:Port Tuple (string → HostPort or separate Host/Port)
- Connection string components
- Used in:
  - AppConfiguration

---

## 11. Design Principles from Codebase

### Principle 1: Result Pattern for Fallible Operations
- `DurationParser.Parse()` returns `Result<TimeSpan, DurationParseError>`
- `ServiceDiscovery.FindServiceProcessIdAsync()` returns `Result<int, string>`
- `DatabaseCleaner.ClearDatabaseAsync()` returns `Result<Unit, ClearDatabaseError>`

### Principle 2: Dunet Union Types for Structured Errors
```csharp
[Union]
public partial record DurationParseError
{
    public partial record Empty;
    public partial record InvalidFormat(string Input);
    public partial record UnknownUnit(char Unit);
}
```

### Principle 3: Using Static for Constructor Shorthand
```csharp
using static JoanComasFdz.Result.Result<System.TimeSpan, DurationParseError>;
return new Success(value);  // Instead of new Result<TimeSpan, DurationParseError>.Success(value)
```

### Principle 4: Validation at Boundary
- CLI args come in as raw primitives
- Validated before passing to business logic
- Orchestration layer receives validated configuration

### Principle 5: Vertical Slice Architecture
- Each slice owns its models and interfaces
- No central "Core" project
- Minimal cross-slice dependencies

---

## 12. Conclusion: Recommended Next Steps

### For Value Object Introduction:
1. **Start with TimeSpan** (already exists as TimeSpan, just need wrapper)
   - Create `Duration` value object
   - Wrap around TimeSpan
   - Use Result<Duration, DurationParseError> pattern

2. **Add Port Number** (frequently used, clear validation rules)
   - Create `Port` value object (1-65535)
   - Wrap around ushort or int
   - Replace int in TestConfiguration.ServicePort

3. **Add EventCount** (core business concept)
   - Create `EventCount` value object (1-1,000,000)
   - Replace int in TestConfiguration.EventCount

4. **Gradually expand** to other primitives following same pattern

### Key Constraint:
- Must work with System.CommandLine (may need custom binders)
- Must serialize/deserialize from JSON (test reports)
- Must maintain backward compatibility with existing code

### Pattern to Follow:
```csharp
// Define union for errors (Dunet)
[Union]
public partial record PortError { ... }

// Create value object
public sealed record Port
{
    public ushort Value { get; }
    
    public static Result<Port, PortError> Create(int value)
    {
        if (value < 1 || value > 65535)
            return new Failure(new PortError.OutOfRange(value));
        return new Success(new Port((ushort)value));
    }
}

// Use in configuration
public record TestConfiguration(Port ServicePort, ...);

// Use in CLI
var portResult = Port.Create(parsedValue);
```
