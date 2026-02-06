# Enable Native AOT Compilation for Performance Tester

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Enable Native AOT (Ahead-of-Time) compilation for the `PerformanceTester.Cli` executable, producing a self-contained native binary that starts faster, uses less memory, and requires no .NET runtime to be installed.

**Architecture:** The solution has 24 projects (14 production + 10 test). Only the `PerformanceTester.Cli` project needs `<PublishAot>true</PublishAot>` since it is the sole executable. All library projects it references must be AOT-compatible (no reflection, source-generated JSON serialization, etc.). Test projects are excluded from AOT (they remain JIT-compiled).

**Tech Stack:** .NET 9.0 (current) with Native AOT, System.Text.Json source generators, programmatic Serilog configuration

---

## AOT Compatibility Assessment

### Current State

The codebase has **no existing AOT configuration** - clean slate. Key findings from the analysis:

| Aspect | Current State | AOT Ready? | Action Required |
|--------|--------------|------------|-----------------|
| JSON serialization | Reflection-based `JsonSerializer` | No | Add source generators |
| Host builder | `Host.CreateDefaultBuilder()` | Partial | Replace with `CreateApplicationBuilder` |
| Serilog config | `ReadFrom.Configuration()` (reflection) | No | Switch to programmatic config |
| System.CommandLine | Beta 4 + Hosting alpha | Partial | Decouple from Hosting package |
| DI registration | Manual/explicit (no scanning) | Yes | No changes needed |
| Configuration reading | Manual indexer `config["KEY"]` | Yes | No changes needed |
| Process launching | `Process.Start()` | Yes | No changes needed |
| Docker.DotNet | Third-party, likely uses reflection | Unknown | Test; add suppressions if needed |
| ScottPlot | Confirmed incompatible with AOT | No | Isolate behind runtime guard |
| CloudNative.CloudEvents | Uses System.Text.Json internally | Unknown | Test; may need annotations |
| RabbitMQ.Client 7.0 | Modern, designed for AOT | Yes | No changes needed |
| Npgsql 9.0 | AOT-ready | Yes | No changes needed |
| Polly 8.0 | AOT-compatible | Yes | No changes needed |
| MathNet.Numerics | Unknown AOT support | Unknown | Test; basic math likely works |
| PerformanceTester.Common | Targets `netstandard2.0` | Yes | No changes needed |

### Critical Blockers

1. **ScottPlot** - Confirmed incompatible (SkiaSharp native library loading issues in AOT). Used in `PerformanceTester.Reporting` for chart generation.
2. **Serilog.Settings.Configuration** - Uses reflection to parse sink definitions from appsettings.json.
3. **System.CommandLine.Hosting** - Alpha package that requires `Host.CreateDefaultBuilder`, which enables reflection-heavy defaults.
4. **All JSON serialization** - 13 serialize + 9 deserialize call sites using reflection-based `JsonSerializer`.

### Strategy: Pragmatic AOT with Trim Warnings Suppressed for Third-Party Libraries

Rather than replacing every third-party library, we will:
1. Fix all **first-party code** to be fully AOT-compatible (JSON source generators, programmatic config, etc.)
2. **Suppress trim warnings** from third-party libraries (Docker.DotNet, ScottPlot, CloudNative.CloudEvents) where replacement would be disproportionate effort
3. Test that the published AOT binary works correctly at runtime despite warnings
4. Track third-party library AOT support for future cleanup

This is the standard approach recommended by Microsoft for migrating large applications to AOT incrementally.

---

## Model/Type Inventory for JSON Source Generators

The following types need `[JsonSerializable]` registration across the codebase:

### PerformanceTester.Reporting (primary serialization hub)

**Report models** (all in `ReportGeneration/Models/`):
- `TestReport`, `PhaseTimestamps`, `MonitoredProcess`, `TestConfiguration`, `TestResults`
- `PublishResults`, `ConsumeResults`, `ApiResults`
- `ThroughputReport`, `ThroughputSampleJson`, `ThroughputSummary`
- `ResourceMetricsReport`, `ProcessResourceMetricsReport`
- `ResourceSampleJson`, `ProcessResourceSampleJson`, `ResourceSummary`

**Shared models** (all in `Shared/Models/`):
- `ProcessResourceSample`, `SystemResourceSample`, `ThroughputMetricSample`
- `ContainerResourceSample`, `ContainerInfo`

**System info models** (in `SystemInfoDetection/Models/`):
- `SystemInfo`, `CpuInfo`, `RamInfo`, `DiskInfo`

**Internal DTOs** (private classes in system info detectors):
- `PhysicalDiskInfo` (WindowsSystemInfoDetector) - must be made `internal`
- `WslMemoryInfo`, `WslDiskInfo` (LinuxSystemInfoDetector) - must be made `internal`

### PerformanceTester.ApiLoadTesting

- `K6Metric`, `K6MetricData`

### PerformanceTester.Cli

- Needs a context that references the Reporting context for `TestReport` deserialization in `CompareCommand`

---

### Task 1: Create Directory.Build.props with AOT-related settings

**Files:**
- Create: `performance-tester-dotnet/src/Directory.Build.props`

**Why:** Centralizes common build settings and enables AOT-related analyzers across all projects. This is best practice for multi-project solutions.

**Content:**

```xml
<Project>

  <!-- AOT compatibility analyzers for all projects -->
  <PropertyGroup>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>

  <!-- Exclude test projects from AOT compatibility requirements -->
  <PropertyGroup Condition="'$(IsTestProject)' == 'true' or '$(IsPackable)' == 'false'">
    <IsAotCompatible>false</IsAotCompatible>
  </PropertyGroup>

</Project>
```

**Notes:**
- `IsAotCompatible=true` enables the `IL2xxx` trimming/AOT analyzers at build time so we get warnings during normal `dotnet build` (not just during `dotnet publish`)
- Test projects are excluded since they remain JIT-compiled
- This does NOT enable AOT publishing - that's separate (`PublishAot` in the CLI project only)

**Verification:** Run `dotnet build` from solution root. Expect new AOT-related warnings that subsequent tasks will fix.

---

### Task 2: Add JSON source generators to PerformanceTester.Reporting

**Files:**
- Create: `src/PerformanceTester.Reporting/ReportGeneration/ReportJsonContext.cs`
- Modify: `src/PerformanceTester.Reporting/ReportGeneration/ReportGenerator.cs` (update `JsonOptions` to use source-generated context)
- Modify: `src/PerformanceTester.Reporting/SystemInfoDetection/WindowsSystemInfoDetector.cs` (make `PhysicalDiskInfo` internal)
- Modify: `src/PerformanceTester.Reporting/SystemInfoDetection/LinuxSystemInfoDetector.cs` (make `WslMemoryInfo` and `WslDiskInfo` internal)

**Step 1: Make private DTO classes internal (required for source generators)**

Source generators cannot see `private` classes nested in other classes. The following DTOs used in JSON deserialization must be promoted to `internal` top-level or nested `internal` classes:

In `WindowsSystemInfoDetector.cs`, change `PhysicalDiskInfo` from private nested class to `internal` (either nested or in its own file).

In `LinuxSystemInfoDetector.cs`, change `WslMemoryInfo` and `WslDiskInfo` from private nested classes to `internal`.

**Step 2: Create the source-generated JSON context**

Create `src/PerformanceTester.Reporting/ReportGeneration/ReportJsonContext.cs`:

```csharp
using System.Text.Json.Serialization;
using PerformanceTester.Reporting.ReportGeneration.Models;
using PerformanceTester.Reporting.Shared.Models;
using PerformanceTester.Reporting.SystemInfoDetection.Models;

namespace PerformanceTester.Reporting.ReportGeneration;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TestReport))]
[JsonSerializable(typeof(ThroughputReport))]
[JsonSerializable(typeof(ProcessResourceMetricsReport))]
[JsonSerializable(typeof(ResourceMetricsReport))]
[JsonSerializable(typeof(SystemInfo))]
[JsonSerializable(typeof(List<PhysicalDiskInfo>))]
[JsonSerializable(typeof(List<WslMemoryInfo>))]
[JsonSerializable(typeof(List<WslDiskInfo>))]
internal partial class ReportJsonContext : JsonSerializerContext
{
}
```

**Note on `Iso8601DateTimeConverter`:** Source-generated contexts do not support custom converters in the `[JsonSourceGenerationOptions]` attribute. The `Iso8601DateTimeConverter` formats DateTime as `yyyy-MM-ddTHH:mm:ss.ffffff`. There are two approaches:

- **Option A (recommended):** Remove the custom converter and let `System.Text.Json` use its default ISO 8601 format (`yyyy-MM-ddTHH:mm:ss.fffffffZ`). This is a minor format change in output files. If this is acceptable, simply omit the converter.
- **Option B:** Keep a hybrid approach - use the source-generated context for type metadata but pass `JsonSerializerOptions` with the custom converter at runtime. This works but means serialization falls back to reflection for the converter. Create the options like:
  ```csharp
  private static readonly JsonSerializerOptions JsonOptions = new(ReportJsonContext.Default.Options)
  {
      Converters = { new Iso8601DateTimeConverter() }
  };
  ```
  Then use `JsonSerializer.Serialize(report, JsonOptions)` instead of passing the context directly.

Choose Option A unless the exact DateTime format is critical for compatibility with the Python reference implementation.

**Step 3: Update ReportGenerator.cs to use the source-generated context**

Replace the static `JsonOptions` field:

```csharp
// Before:
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new Iso8601DateTimeConverter() }
};

// After (Option A - drop custom converter):
// Remove the JsonOptions field entirely. Use ReportJsonContext.Default directly.

// After (Option B - keep custom converter):
private static readonly JsonSerializerOptions JsonOptions = new(ReportJsonContext.Default.Options)
{
    Converters = { new Iso8601DateTimeConverter() }
};
```

Update all 7 serialization call sites (lines ~121, 164, 207, 252, 302, 351, 400) to use the appropriate typed context or options. For example:

```csharp
// Option A: Direct context usage
var json = JsonSerializer.Serialize(mainReport, ReportJsonContext.Default.TestReport);

// Option B: Options with converter fallback
var json = JsonSerializer.Serialize(mainReport, JsonOptions);
```

**Step 4: Update deserialization in SystemInfoDetectors**

In `WindowsSystemInfoDetector.cs` (lines ~217, 222):
```csharp
// Before:
var disks = JsonSerializer.Deserialize<List<PhysicalDiskInfo>>(json);

// After:
var disks = JsonSerializer.Deserialize(json, ReportJsonContext.Default.ListPhysicalDiskInfo);
```

In `LinuxSystemInfoDetector.cs` (lines ~290, 432):
```csharp
// Before:
var memInfo = JsonSerializer.Deserialize<List<WslMemoryInfo>>(json);

// After:
var memInfo = JsonSerializer.Deserialize(json, ReportJsonContext.Default.ListWslMemoryInfo);
```

**Verification:** `dotnet build src/PerformanceTester.Reporting` should compile with no new warnings. Check that the source generator produces the expected `ReportJsonContext` partial class.

---

### Task 3: Add JSON source generators to PerformanceTester.ApiLoadTesting

**Files:**
- Create: `src/PerformanceTester.ApiLoadTesting/K6JsonContext.cs`
- Modify: `src/PerformanceTester.ApiLoadTesting/K6MetricsParser.cs`

**Step 1: Create K6JsonContext.cs**

```csharp
using System.Text.Json.Serialization;

namespace PerformanceTester.ApiLoadTesting;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(K6Metric))]
internal partial class K6JsonContext : JsonSerializerContext
{
}
```

**Step 2: Update K6MetricsParser.cs**

```csharp
// Before (line ~36):
var metric = JsonSerializer.Deserialize<K6Metric>(line, JsonOptions);

// After:
var metric = JsonSerializer.Deserialize(line, K6JsonContext.Default.K6Metric);
```

Remove the static `JsonOptions` field since the options are now embedded in the source-generated context.

**Verification:** `dotnet build src/PerformanceTester.ApiLoadTesting` should compile cleanly.

---

### Task 4: Add JSON source generator to PerformanceTester.Cli for CompareCommand

**Files:**
- Create: `src/PerformanceTester.Cli/CliJsonContext.cs`
- Modify: `src/PerformanceTester.Cli/Commands/CompareCommand.cs`

**Step 1: Create CliJsonContext.cs**

```csharp
using System.Text.Json.Serialization;
using PerformanceTester.Reporting.ReportGeneration.Models;

namespace PerformanceTester.Cli;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(TestReport))]
internal partial class CliJsonContext : JsonSerializerContext
{
}
```

**Note:** This context is separate from `ReportJsonContext` because `CompareCommand` needs `PropertyNameCaseInsensitive = true` for reading files, while `ReportGenerator` does not. The same custom converter consideration from Task 2 applies here.

**Step 2: Update CompareCommand.cs**

```csharp
// Before (line ~112):
var report = JsonSerializer.Deserialize<TestReport>(json, JsonOptions);

// After (Option A):
var report = JsonSerializer.Deserialize(json, CliJsonContext.Default.TestReport);

// After (Option B - with custom converter):
private static readonly JsonSerializerOptions JsonOptions = new(CliJsonContext.Default.Options)
{
    Converters = { new Iso8601DateTimeConverter() }
};
var report = JsonSerializer.Deserialize<TestReport>(json, JsonOptions);
```

Remove the old static `JsonOptions` field if using Option A.

**Verification:** `dotnet build src/PerformanceTester.Cli` should compile cleanly.

---

### Task 5: Replace Serilog reflection-based configuration with programmatic setup

**Files:**
- Modify: `src/PerformanceTester.Cli/Program.cs`
- Modify: `src/PerformanceTester.Cli/PerformanceTester.Cli.csproj` (remove `Serilog.Settings.Configuration` package)

**Why:** `Serilog.Settings.Configuration` uses reflection to resolve sink types from appsettings.json string names (e.g., `"Name": "File"`). This is incompatible with AOT. The file logging configuration currently in appsettings.json must move to code.

**Step 1: Read current appsettings.json logging config**

The current appsettings.json contains Serilog configuration for file logging:
```json
{
  "Serilog": {
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "logs/performance-tester-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7,
          "outputTemplate": "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ]
  }
}
```

**Step 2: Replace with programmatic configuration in Program.cs**

Change the `.UseSerilog()` call in `ConfigureHost()`:

```csharp
// Before:
.UseSerilog((context, services, loggerConfig) =>
{
    const string consoleTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";
    loggerConfig
        .ReadFrom.Configuration(context.Configuration)  // <-- reflection-based
        .WriteTo.Sink(new ProgressAwareConsoleSink(consoleTemplate))
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId();
})

// After:
.UseSerilog((context, services, loggerConfig) =>
{
    const string consoleTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";
    const string fileTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}";
    loggerConfig
        .MinimumLevel.Information()
        .WriteTo.Sink(new ProgressAwareConsoleSink(consoleTemplate))
        .WriteTo.File(
            path: "logs/performance-tester-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7,
            outputTemplate: fileTemplate)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId();
})
```

**Step 3: Remove the Serilog.Settings.Configuration package**

In `PerformanceTester.Cli.csproj`, remove:
```xml
<PackageReference Include="Serilog.Settings.Configuration" Version="9.0.0" />
```

**Step 4: Clean up appsettings.json**

Remove the `"Serilog"` section from `appsettings.json`. If the file becomes empty or contains only `{}`, either delete it or keep it with only non-Serilog configuration.

**Verification:** `dotnet build src/PerformanceTester.Cli` should compile cleanly. Run the CLI to verify logging still works to both console and file.

---

### Task 6: Decouple CLI from System.CommandLine.Hosting and use AOT-compatible host builder

**Files:**
- Modify: `src/PerformanceTester.Cli/Program.cs`
- Modify: `src/PerformanceTester.Cli/PerformanceTester.Cli.csproj`

**Why:** `System.CommandLine.Hosting` (alpha package) forces use of `Host.CreateDefaultBuilder()`, which enables reflection-heavy defaults. For AOT, we need `Host.CreateApplicationBuilder()` (or `HostApplicationBuilder`), and we need to decouple command parsing from host building.

**Step 1: Remove System.CommandLine.Hosting package**

In `PerformanceTester.Cli.csproj`, remove:
```xml
<PackageReference Include="System.CommandLine.Hosting" Version="0.4.0-alpha.22272.1" />
```

**Step 2: Restructure Program.cs to separate command parsing from host building**

The key change is: parse commands first, then build the host, then execute the command handler with the host.

```csharp
// Current pattern (coupled):
var parser = new CommandLineBuilder(rootCommand)
    .UseDefaults()
    .UseHost(_ => Host.CreateDefaultBuilder(args), ConfigureHost)  // coupled!
    .Build();
return await parser.InvokeAsync(args);

// New pattern (decoupled):
// 1. Build the host independently
var host = BuildHost(args);

// 2. Parse and execute commands, passing the host to handlers
var rootCommand = BuildRootCommand(host);
return await rootCommand.InvokeAsync(args);
```

The command handlers (TestCommand, CompareCommand) currently get the host via `context.GetHost()`. After decoupling, they receive services directly (via closure or parameter). For example:

```csharp
// Before (in TestCommand):
command.SetHandler(async (InvocationContext context) =>
{
    var host = context.GetHost();
    var orchestrator = host.Services.GetRequiredService<ITestOrchestrator>();
    // ...
});

// After:
public static Command Create(IHost host)
{
    // ... option definitions ...
    command.SetHandler(async (InvocationContext context) =>
    {
        var orchestrator = host.Services.GetRequiredService<ITestOrchestrator>();
        // ...
    });
    return command;
}
```

**Step 3: Use `Host.CreateApplicationBuilder()` instead of `CreateDefaultBuilder()`**

```csharp
private static IHost BuildHost(string[] args)
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        DisableDefaults = true  // Disable reflection-heavy defaults
    });

    builder.Configuration.SetBasePath(AppContext.BaseDirectory);
    builder.Configuration.AddJsonFile("appsettings.json", optional: true);
    builder.Configuration.AddEnvironmentVariables("PERFTEST_");

    // Serilog (programmatic, from Task 5)
    const string consoleTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";
    const string fileTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}";
    builder.Services.AddSerilog(loggerConfig =>
    {
        loggerConfig
            .MinimumLevel.Information()
            .WriteTo.Sink(new ProgressAwareConsoleSink(consoleTemplate))
            .WriteTo.File(
                path: "logs/performance-tester-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: fileTemplate)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId();
    });

    var appConfig = AppConfiguration.Load(builder.Configuration);
    builder.Services.AddSingleton(appConfig);
    builder.Services.AddOrchestration(
        postgresConnectionString: appConfig.PostgresConnectionString,
        rabbitMqConnectionString: appConfig.RabbitMqConnectionString,
        rabbitMqContainerName: appConfig.RabbitMqContainerName,
        postgresContainerName: appConfig.PostgresContainerName);
    builder.Services.AddSingleton<IConsoleWriter, ConsoleWriter>();
    builder.Services.AddSingleton<IProgressReporter, ProgressReporter>();

    return builder.Build();
}
```

**Step 4: Update Main method**

```csharp
public static async Task<int> Main(string[] args)
{
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Sink(new ProgressAwareConsoleSink(outputTemplate))
        .CreateBootstrapLogger();

    try
    {
        var host = BuildHost(args);
        var rootCommand = BuildRootCommand(host);
        return await rootCommand.InvokeAsync(args);
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Application terminated unexpectedly");
        return 1;
    }
    finally
    {
        await Log.CloseAndFlushAsync();
    }
}
```

**Verification:** `dotnet build src/PerformanceTester.Cli`. Then run `dotnet run --project src/PerformanceTester.Cli -- --help` to verify CLI still works. Test at least one command end-to-end.

---

### Task 7: Handle anonymous type serialization in IntegrationTesting

**Files:**
- Modify: `src/PerformanceTester.IntegrationTesting/RabbitMQ.cs`

**Why:** Anonymous types (`new { configure = ".*", ... }`) cannot be used with source-generated JSON serialization because source generators need concrete types known at compile time.

**Step 1: Replace anonymous type with a concrete record**

In `RabbitMQ.cs`, near line 294-299, there is an anonymous object serialized for RabbitMQ HTTP API permissions:

```csharp
// Before:
var body = JsonSerializer.Serialize(new { configure = ".*", write = ".*", read = ".*" });

// After - define a record (can be in the same file, internal):
internal record RabbitMqPermissions(string Configure, string Write, string Read);

// And use it:
var body = JsonSerializer.Serialize(
    new RabbitMqPermissions(".*", ".*", ".*"),
    IntegrationTestingJsonContext.Default.RabbitMqPermissions);
```

**Step 2: Create IntegrationTestingJsonContext**

Create `src/PerformanceTester.IntegrationTesting/IntegrationTestingJsonContext.cs`:

```csharp
using System.Text.Json.Serialization;

namespace PerformanceTester.IntegrationTesting;

[JsonSerializable(typeof(RabbitMqPermissions))]
internal partial class IntegrationTestingJsonContext : JsonSerializerContext
{
}
```

**Note:** `PerformanceTester.IntegrationTesting` is used only by test projects, but making it AOT-compatible is still good practice and prevents warnings from `IsAotCompatible=true` in Directory.Build.props. If this project has `<IsPackable>false</IsPackable>`, the analyzers won't fire anyway, in which case this task is optional.

**Verification:** `dotnet build src/PerformanceTester.IntegrationTesting`.

---

### Task 8: Enable PublishAot in PerformanceTester.Cli

**Files:**
- Modify: `src/PerformanceTester.Cli/PerformanceTester.Cli.csproj`

**Step 1: Add AOT publishing properties**

Add the following to the main `<PropertyGroup>`:

```xml
<PublishAot>true</PublishAot>
<InvariantGlobalization>false</InvariantGlobalization>
```

**Notes on settings:**
- `PublishAot=true` - Enables native AOT compilation when running `dotnet publish`
- `InvariantGlobalization=false` - Keep full globalization support (needed for date formatting, culture-specific output). Setting to `true` would reduce binary size but break locale-dependent features.
- Do NOT add `<SelfContained>true</SelfContained>` - this is implicit with `PublishAot`
- Do NOT add `<PublishSingleFile>` - incompatible with AOT

**Step 2: Add trim warning suppressions for third-party libraries**

Some third-party libraries will produce trim warnings. Add suppressions for known-working libraries:

```xml
<!-- Suppress trim warnings from third-party libraries that work at runtime despite warnings -->
<ItemGroup>
  <TrimmerRootAssembly Include="Serilog" />
  <TrimmerRootAssembly Include="Serilog.Sinks.File" />
</ItemGroup>
```

If Docker.DotNet or ScottPlot produce warnings that cannot be suppressed cleanly, add:

```xml
<PropertyGroup>
  <!-- Suppress specific trim warning categories from third-party code -->
  <NoWarn>$(NoWarn);IL2026;IL2057;IL2072;IL2075;IL2104</NoWarn>
</PropertyGroup>
```

**Important:** Only suppress warnings from third-party code. All first-party code (our source) should be warning-free after Tasks 2-7.

**Verification:** Run `dotnet publish src/PerformanceTester.Cli -c Release -r linux-x64` (or appropriate RID). This will:
1. Compile all projects
2. Run the IL linker (trimmer)
3. Run the NativeAOT compiler (ILC)
4. Produce a native binary at `src/PerformanceTester.Cli/bin/Release/net9.0/linux-x64/publish/performance-tester`

Note the warnings produced. First-party warnings should be zero. Third-party warnings should be reviewed and suppressed as appropriate.

---

### Task 9: Test the AOT-compiled binary

**Files:**
- No files modified (testing only)

**Step 1: Publish the native binary**

```bash
cd /workspace/performance-tester-dotnet
dotnet publish src/PerformanceTester.Cli -c Release -r linux-x64
```

Expected output location: `src/PerformanceTester.Cli/bin/Release/net9.0/linux-x64/publish/performance-tester`

**Step 2: Verify the binary**

```bash
# Check it's a native binary (not a .NET assembly)
file src/PerformanceTester.Cli/bin/Release/net9.0/linux-x64/publish/performance-tester
# Expected: "ELF 64-bit LSB executable" (not "PE32 executable" or ".NET assembly")

# Check binary size
ls -lh src/PerformanceTester.Cli/bin/Release/net9.0/linux-x64/publish/performance-tester

# Test --help
./src/PerformanceTester.Cli/bin/Release/net9.0/linux-x64/publish/performance-tester --help

# Test --version (if implemented)
./src/PerformanceTester.Cli/bin/Release/net9.0/linux-x64/publish/performance-tester --version
```

**Step 3: Functional test**

If Docker infrastructure is running, test a real operation:

```bash
./src/PerformanceTester.Cli/bin/Release/net9.0/linux-x64/publish/performance-tester test --events 10 --api-duration 5s
```

**Step 4: Compare startup time**

```bash
# AOT binary
time ./src/PerformanceTester.Cli/bin/Release/net9.0/linux-x64/publish/performance-tester --help

# JIT (for comparison)
time dotnet run --project src/PerformanceTester.Cli -- --help
```

**Step 5: Document results**

Record:
- Binary size (expected: 30-80 MB depending on dependencies)
- Startup time improvement
- Any runtime errors or missing functionality
- List of suppressed warnings

**Verification:** The AOT binary should produce identical output to `dotnet run` for all commands.

---

### Task 10: Add AOT publish profile and update build documentation

**Files:**
- Create: `src/PerformanceTester.Cli/Properties/PublishProfiles/linux-x64-aot.pubxml`
- Modify: `performance-tester-dotnet/README.md` (add AOT build instructions)

**Step 1: Create publish profile**

Create `src/PerformanceTester.Cli/Properties/PublishProfiles/linux-x64-aot.pubxml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

**Step 2: Update README.md**

Add a section documenting both build modes:

```markdown
### Building

#### Standard Build (JIT)
```bash
dotnet build
```

#### Native AOT Build
```bash
# Linux x64
dotnet publish src/PerformanceTester.Cli -c Release -r linux-x64

# The native binary will be at:
# src/PerformanceTester.Cli/bin/Release/net9.0/linux-x64/publish/performance-tester
```

**Benefits of AOT build:**
- No .NET runtime required on target machine
- Faster startup time (~10x improvement)
- Lower memory footprint
- Single self-contained binary
```

**Verification:** Ensure the publish profile works: `dotnet publish src/PerformanceTester.Cli -c Release -p:PublishProfile=linux-x64-aot`.

---

### Task 11: Run the full test suite to verify no regressions

**Files:**
- No files modified (verification only)

**Step 1: Build the entire solution**

```bash
cd /workspace/performance-tester-dotnet
dotnet build
```

Verify: zero errors, zero warnings from first-party code. Third-party AOT analyzer warnings are expected and acceptable.

**Step 2: Run all unit tests**

```bash
dotnet test
```

All existing tests must continue to pass. The source generators and configuration changes should not affect test behavior since tests run in JIT mode.

**Step 3: Verify AOT publish still succeeds**

```bash
dotnet publish src/PerformanceTester.Cli -c Release -r linux-x64
```

Must produce a valid native binary without errors (warnings from third-party code are acceptable).

**Verification:** All tests pass. AOT publish succeeds. No regressions.

---

## Risk Mitigation

### If ScottPlot causes AOT publish failure

ScottPlot is confirmed incompatible with Native AOT. If it causes the ILC (NativeAOT compiler) to fail entirely:

1. **Add `<PublishAot Condition="...">` to selectively disable AOT** - not ideal
2. **Use `[UnconditionalSuppressMessage]` on the charting code paths** - suppresses linker warnings
3. **Move charting to a separate process** - the chart generation code in Reporting could be invoked as a separate `dotnet run` command, keeping the main CLI as AOT
4. **Replace ScottPlot** - Consider alternatives like generating SVG manually, using gnuplot CLI, or deferring chart generation to a post-processing step

The most likely outcome is that ScottPlot will produce trim warnings but still work at runtime, since chart generation uses SkiaSharp's native rendering which doesn't rely on reflection. Test this during Task 9.

### If Docker.DotNet causes runtime failures in AOT

1. **Replace with CLI-based Docker monitoring** - Shell out to `docker stats --no-stream --format '{{json .}}'` and parse the JSON output. This is fully AOT-compatible.
2. **Replace with Docker REST API** - Use `HttpClient` to call the Docker API directly at `unix:///var/run/docker.sock`. The REST API returns JSON that can be deserialized with source-generated serializers.

### If System.CommandLine produces AOT issues

1. **Test thoroughly** - System.CommandLine 2.0+ should work with AOT for basic scenarios
2. **Fallback: Replace with raw argument parsing** - The CLI has only 2 commands (`test`, `compare`) with simple options. Manual parsing with `args` array is feasible but less elegant.
3. **Alternative library: Spectre.Console.Cli** - Well-known, actively maintained, and has explicit AOT support.

---

## Future Improvements (Out of Scope)

These are noted for future consideration but are NOT part of this plan:

1. **Upgrade to .NET 10** - .NET 10 has improved AOT support with smaller binaries and faster compilation. Consider upgrading after this plan is implemented (see `2026-02-06-dotnet-10-upgrade.md`).
2. **Replace Docker.DotNet with direct API calls** - Eliminates a third-party AOT risk entirely.
3. **Replace ScottPlot with AOT-compatible charting** - When an AOT-compatible charting library becomes available.
4. **Profile-Guided Optimization (PGO)** - Use instrumented builds to optimize hot paths in the AOT binary.
5. **Binary size optimization** - Use `<IlcOptimizationPreference>Size</IlcOptimizationPreference>` and `<InvariantGlobalization>true</InvariantGlobalization>` if locale support is not critical.
