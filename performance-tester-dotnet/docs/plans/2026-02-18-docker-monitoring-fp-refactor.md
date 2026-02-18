# Docker Monitoring FP Refactor Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace `IDockerMonitor` interface with named delegates (`WarmupDockerMonitors`, `StartDockerMonitoring`, `GetDockerMetrics`, `ReportDockerMonitorProgress`), keeping BackgroundService internals intact.

**Architecture:** DockerMonitoring project gains an Infrastructure reference for `NonEmptyString`. The `AddDockerMonitoring` extension accepts `params NonEmptyString[]` and registers three operational delegates + internal BackgroundServices. Consumers (Orchestration phases, tests) resolve only delegates — never interfaces or service instances.

**Tech Stack:** .NET 9, Microsoft.Extensions.DependencyInjection (keyed services), xUnit, JoanComasFdz.AssertingThat

**Design doc:** `docs/plans/2026-02-18-docker-monitoring-fp-refactor-design.md`

**Coding guidelines:** `CODING_GUIDELINES.md` (Guidelines 12–14, 20, 25–28, 29)

---

### Task 1: Add Infrastructure project reference and define named delegates

**Files:**
- Modify: `src/PerformanceTester.DockerMonitoring/PerformanceTester.DockerMonitoring.csproj`
- Create: `src/PerformanceTester.DockerMonitoring/DockerMonitoringDelegates.cs`

**Step 1: Add project reference to Infrastructure**

In `PerformanceTester.DockerMonitoring.csproj`, add an `<ItemGroup>` with a `<ProjectReference>` to `PerformanceTester.Infrastructure`:

```xml
  <ItemGroup>
    <ProjectReference Include="..\PerformanceTester.Infrastructure\PerformanceTester.Infrastructure.csproj" />
  </ItemGroup>
```

**Step 2: Create delegate definitions file**

Create `src/PerformanceTester.DockerMonitoring/DockerMonitoringDelegates.cs`:

```csharp
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Reports docker monitoring phase changes.
/// Replaces IProgress&lt;DockerMonitorPhaseInfo&gt; with a named delegate per Guideline 13.
/// </summary>
public delegate void ReportDockerMonitorProgress(DockerMonitorPhaseInfo phaseInfo);

/// <summary>
/// Warms up Docker API for all registered containers.
/// First Docker API call is typically slow (~2-3s); this avoids measurement delays.
/// </summary>
public delegate Task WarmupDockerMonitors(CancellationToken ct = default);

/// <summary>
/// Starts metrics collection on all registered containers.
/// Blocks until the first sample is collected per container.
/// </summary>
public delegate Task StartDockerMonitoring(
    ReportDockerMonitorProgress reportProgress,
    CancellationToken ct = default);

/// <summary>
/// Retrieves collected metrics for a specific container by name.
/// Returns metrics in chronological order.
/// </summary>
public delegate IReadOnlyCollection<DockerMetrics> GetDockerMetrics(
    NonEmptyString containerName);
```

**Step 3: Verify build**

Run: `dotnet build src/PerformanceTester.DockerMonitoring`
Expected: Build succeeds (delegates defined, no consumers yet)

**Step 4: Commit**

```bash
git add src/PerformanceTester.DockerMonitoring/PerformanceTester.DockerMonitoring.csproj \
        src/PerformanceTester.DockerMonitoring/DockerMonitoringDelegates.cs
git commit -m "feat(docker-monitoring): add Infrastructure ref and define named delegates"
```

---

### Task 2: Update DockerMonitorService to use ReportDockerMonitorProgress

**Files:**
- Modify: `src/PerformanceTester.DockerMonitoring/DockerMonitorService.cs`

The internal `DockerMonitorService` currently stores `IProgress<DockerMonitorPhaseInfo>? _progress` and calls `_progress?.Report(...)`. Change it to store `ReportDockerMonitorProgress? _progress` and call `_progress?.Invoke(...)`.

**Step 1: Update the field and parameter types**

In `DockerMonitorService.cs`:

1. Change the field from:
```csharp
    private IProgress<DockerMonitorPhaseInfo>? _progress;
```
to:
```csharp
    private ReportDockerMonitorProgress? _progress;
```

2. Change the `StartMonitoringAsync` signature from:
```csharp
    public async Task StartMonitoringAsync(
        IProgress<DockerMonitorPhaseInfo>? progress = null,
        CancellationToken cancellationToken = default)
```
to:
```csharp
    public async Task StartMonitoringAsync(
        ReportDockerMonitorProgress? progress = null,
        CancellationToken cancellationToken = default)
```

**Step 2: Replace all `_progress?.Report(...)` with `_progress?.Invoke(...)`**

There are 8 occurrences of `_progress?.Report(...)` in `DockerMonitorService.cs`. Replace each with `_progress?.Invoke(...)`. The arguments remain identical — only the call mechanism changes.

Occurrences (search for `_progress?.Report(`):
1. In `StartMonitoringAsync` — `_progress?.Report(DockerMonitorPhaseInfo.Starting(...))` → `_progress?.Invoke(DockerMonitorPhaseInfo.Starting(...))`
2. In `ExecuteAsync` (container resolve failed) — same pattern
3. In `ExecuteAsync` (container not found) — same pattern
4. In `ExecuteAsync` finally block — same pattern
5. In `RunStreamingLoopWithReconnectionAsync` (connecting) — same pattern
6. In `HandleConnectionSuccess` (connected) — same pattern
7. In `ShouldRetry` (failed permanently) — same pattern
8. In `AttemptReconnectionAsync` (disconnected) — same pattern

**Step 3: Remove the `IDockerMonitor` interface implementation**

`DockerMonitorService` currently declares `: BackgroundService, IDockerMonitor`. Remove `, IDockerMonitor`:

```csharp
// Before:
internal sealed class DockerMonitorService : BackgroundService, IDockerMonitor

// After:
internal sealed class DockerMonitorService : BackgroundService
```

The methods (`WarmupAsync`, `StartMonitoringAsync`, `GetCollectedMetrics`, `ContainerName`) stay on the class — they're still called by the delegates registered in DI. They just no longer satisfy an interface contract.

**Step 4: Verify build of DockerMonitoring project**

Run: `dotnet build src/PerformanceTester.DockerMonitoring`
Expected: Build succeeds. (Other projects will break — we fix them in subsequent tasks.)

**Step 5: Commit**

```bash
git add src/PerformanceTester.DockerMonitoring/DockerMonitorService.cs
git commit -m "refactor(docker-monitoring): use ReportDockerMonitorProgress delegate, drop IDockerMonitor"
```

---

### Task 3: Rewrite ServiceCollectionExtensions and delete IDockerMonitor

**Files:**
- Modify: `src/PerformanceTester.DockerMonitoring/ServiceCollectionExtensions.cs`
- Delete: `src/PerformanceTester.DockerMonitoring/IDockerMonitor.cs`

**Step 1: Rewrite ServiceCollectionExtensions.cs**

Replace the entire file with:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring;

/// <summary>
/// Extension methods for registering Docker monitoring services.
/// Registers BackgroundServices internally and exposes named delegates publicly.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Docker container monitoring for the specified containers.
    /// Registers internal BackgroundServices and three public named delegates:
    /// <see cref="WarmupDockerMonitors"/>, <see cref="StartDockerMonitoring"/>,
    /// and <see cref="GetDockerMetrics"/>.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="containerNames">Names of containers to monitor.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddDockerMonitoring(
        this IServiceCollection services,
        params NonEmptyString[] containerNames)
    {
        // Shared Docker client (singleton, registered once)
        services.AddSingleton<DockerClientWrapper>();

        // Register one BackgroundService per container (keyed singletons)
        containerNames.ToList().ForEach(name =>
        {
            services.AddKeyedSingleton<DockerMonitorService>(name.Value, (sp, _) =>
                new DockerMonitorService(
                    name.Value,
                    sp.GetRequiredService<DockerClientWrapper>(),
                    sp.GetRequiredService<ILogger<DockerMonitorService>>()));

            services.AddSingleton<IHostedService>(sp =>
                sp.GetRequiredKeyedService<DockerMonitorService>(name.Value));
        });

        // Helper: resolve all monitors from DI
        IEnumerable<DockerMonitorService> resolveMonitors(IServiceProvider sp) =>
            containerNames.Select(name =>
                sp.GetRequiredKeyedService<DockerMonitorService>(name.Value));

        // Register the 3 public named delegates
        services.AddSingleton<WarmupDockerMonitors>(sp =>
            ct => Task.WhenAll(resolveMonitors(sp).Select(m => m.WarmupAsync(ct))));

        services.AddSingleton<StartDockerMonitoring>(sp =>
            (progress, ct) => Task.WhenAll(
                resolveMonitors(sp).Select(m => m.StartMonitoringAsync(progress, ct))));

        services.AddSingleton<GetDockerMetrics>(sp =>
            containerName => resolveMonitors(sp)
                .Single(m => m.ContainerName == containerName.Value)
                .GetCollectedMetrics());

        return services;
    }
}
```

**Step 2: Delete IDockerMonitor.cs**

```bash
rm src/PerformanceTester.DockerMonitoring/IDockerMonitor.cs
```

**Step 3: Verify build of DockerMonitoring project**

Run: `dotnet build src/PerformanceTester.DockerMonitoring`
Expected: Build succeeds.

**Step 4: Commit**

```bash
git add src/PerformanceTester.DockerMonitoring/ServiceCollectionExtensions.cs
git rm src/PerformanceTester.DockerMonitoring/IDockerMonitor.cs
git commit -m "refactor(docker-monitoring): rewrite DI registration with delegates, remove IDockerMonitor"
```

---

### Task 4: Update Orchestration ServiceCollectionExtensions

**Files:**
- Modify: `src/PerformanceTester.Orchestration/ServiceCollectionExtensions.cs`

**Step 1: Update the AddDockerMonitoring calls**

The `AddOrchestration` method currently accepts `string rabbitMqContainerName` and `string postgresContainerName` and calls `AddDockerMonitoring` twice (once per container). Change to a single call with `NonEmptyString` values.

The method signature changes: the two container name string params become `NonEmptyString` types. Since `RabbitMqContainerName` and `PostgresContainerName` both inherit from `NonEmptyString`, callers can pass them directly.

Replace the two calls:
```csharp
        // Docker monitoring - register monitors for RabbitMQ and PostgreSQL
        // Orchestrator receives all monitors via IEnumerable<IDockerMonitor>
        services.AddDockerMonitoring(rabbitMqContainerName);
        services.AddDockerMonitoring(postgresContainerName);
```

With a single call:
```csharp
        // Docker monitoring - register monitors and expose named delegates
        services.AddDockerMonitoring(rabbitMqContainerName, postgresContainerName);
```

Also update the method signature parameters from `string` to `NonEmptyString`:

```csharp
    public static IServiceCollection AddOrchestration(
        this IServiceCollection services,
        string postgresConnectionString,
        string rabbitMqConnectionString,
        NonEmptyString rabbitMqContainerName,
        NonEmptyString postgresContainerName)
```

Add the required `using` at the top:
```csharp
using PerformanceTester.Infrastructure.ValueObjects;
```

Remove the `using` that's no longer needed:
```csharp
// Remove this line:
using PerformanceTester.DockerMonitoring;
```

Wait — `DockerMonitoring` is still needed for the delegate types if any are resolved in this file. Check: no, the delegates are resolved in the phase `BuildDependencies` methods, not here. But `AddDockerMonitoring` is an extension method in the `PerformanceTester.DockerMonitoring` namespace, so keep that `using`.

**Step 2: Update XML doc for the parameters**

Update the `<param>` docs:
```csharp
    /// <param name="rabbitMqContainerName">RabbitMQ container name for monitoring.</param>
    /// <param name="postgresContainerName">PostgreSQL container name for monitoring.</param>
```

Remove the default values from the parameters (they were `= "performancetest-rabbitmq"` etc.). NonEmptyString can't have string literal defaults. The caller must provide them.

**Step 3: Update callers of AddOrchestration**

Search for all callers of `AddOrchestration` in the codebase. These will need to pass `NonEmptyString` (or derived types) instead of strings.

Known callers:
- `src/PerformanceTester.Cli/Program.cs` or wherever the CLI wires up DI
- `src/PerformanceTester.Orchestration.IntegrationTests/Infrastructure/OrchestrationSystem.cs`

For each caller, pass the value objects from `TestConfiguration` (which already has `RabbitMqContainerName` and `PostgresContainerName` as value objects).

Run: `grep -rn "AddOrchestration" src/` to find all callers.

**Step 4: Verify solution builds**

Run: `dotnet build src/PerformanceTester.Orchestration`
Expected: May still fail (phases not updated yet). That's OK — continue to next task.

**Step 5: Commit**

```bash
git add src/PerformanceTester.Orchestration/ServiceCollectionExtensions.cs
git commit -m "refactor(orchestration): update AddOrchestration to use NonEmptyString container names"
```

---

### Task 5: Update Orchestration phases to use named delegates

**Files:**
- Modify: `src/PerformanceTester.Orchestration/Phases/SetupPhase.cs`
- Modify: `src/PerformanceTester.Orchestration/Phases/EventTestPhase.cs`
- Modify: `src/PerformanceTester.Orchestration/Phases/ReportingPhase.cs`
- Modify: `src/PerformanceTester.Orchestration/TestOrchestrator.cs`

#### Step 1: Update SetupPhase.BuildDependencies

In `SetupPhase.cs`, the `BuildDependencies` method currently resolves `IEnumerable<IDockerMonitor>`. Replace with `WarmupDockerMonitors` delegate.

Remove:
```csharp
using PerformanceTester.DockerMonitoring;
```

Add:
```csharp
using PerformanceTester.DockerMonitoring;
```

(Keep the using — it's needed for the delegate type `WarmupDockerMonitors`.)

In `BuildDependencies`, replace:
```csharp
        var dockerMonitors = services.GetRequiredService<IEnumerable<IDockerMonitor>>();
```
and:
```csharp
            WarmupDockerApi: () => Task.WhenAll(dockerMonitors.Select(m => m.WarmupAsync(ct))),
```

With:
```csharp
        var warmupDockerMonitors = services.GetRequiredService<WarmupDockerMonitors>();
```
and:
```csharp
            WarmupDockerApi: () => warmupDockerMonitors(ct),
```

#### Step 2: Update EventTestPhase.BuildDependencies

In `EventTestPhase.cs`, the `BuildDependencies` method resolves `IEnumerable<IDockerMonitor>`.

Replace:
```csharp
        var dockerMonitors = services.GetRequiredService<IEnumerable<IDockerMonitor>>();
```
and:
```csharp
            StartDockerMonitoring: () => Task.WhenAll(dockerMonitors.Select(m => m.StartMonitoringAsync(cancellationToken: ct))),
```

With:
```csharp
        var startDockerMonitoring = services.GetRequiredService<DockerMonitoring.StartDockerMonitoring>();
```
and the `StartDockerMonitoring` delegate in the `Dependencies` constructor should now accept a progress callback. The delegate definition in EventTestPhase currently is:
```csharp
    public delegate Task StartDockerMonitoring();
```

This needs to change to accept the progress reporter:
```csharp
    public delegate Task StartDockerMonitoring(ReportDockerMonitorProgress reportProgress);
```

Add `using PerformanceTester.DockerMonitoring;` if not already present.

The wiring becomes:
```csharp
            StartDockerMonitoring: (progress) => startDockerMonitoring(progress, ct),
```

In `ExecuteAsync`, find the call:
```csharp
            await deps.StartDockerMonitoring();
```

This now needs to pass a progress reporter. Currently there's no docker-specific progress in EventTestPhase. For now, create a no-op reporter:
```csharp
            await deps.StartDockerMonitoring(_ => { });
```

Or better — if the orchestrator has a `ReportPhaseProgress` available, we can thread it through. But looking at the current code, EventTestPhase already receives `reportProgress` in `BuildDependencies`. We can create a minimal adapter. For simplicity and to avoid over-engineering, pass a no-op:
```csharp
            await deps.StartDockerMonitoring(_ => { });
```

Wait — the design says progress is non-nullable and must always be provided. The purpose is to receive phase transition info from docker monitors. The orchestrator doesn't currently use this info during EventTestPhase. A no-op delegate is fine.

**Update:** Actually, looking at this more carefully — the `DockerMonitorPhaseAwaiter` in tests wants to receive progress. In production, the orchestrator doesn't currently display docker monitor phase info. So no-op is correct for production. Tests will pass their own awaiter.

#### Step 3: Update ReportingPhase.BuildDependencies

In `ReportingPhase.cs`, replace:
```csharp
        var dockerMonitors = services.GetRequiredService<IEnumerable<IDockerMonitor>>();
```
and:
```csharp
            GetRabbitMqMetrics: () => dockerMonitors
                .Single(m => m.ContainerName == config.RabbitMqContainerName.Value)
                .GetCollectedMetrics(),
            GetPostgresMetrics: () => dockerMonitors
                .Single(m => m.ContainerName == config.PostgresContainerName.Value)
                .GetCollectedMetrics(),
```

With:
```csharp
        var getDockerMetrics = services.GetRequiredService<GetDockerMetrics>();
```
and:
```csharp
            GetRabbitMqMetrics: () => getDockerMetrics(config.RabbitMqContainerName),
            GetPostgresMetrics: () => getDockerMetrics(config.PostgresContainerName),
```

#### Step 4: Clean up TestOrchestrator.cs

Remove the `using PerformanceTester.DockerMonitoring;` line from `TestOrchestrator.cs` if it's no longer needed. Check: `TestOrchestrator` references `DockerMetrics` in `TestResult` — so the using may still be needed. Keep it if `DockerMetrics` is referenced.

#### Step 5: Verify solution builds

Run: `dotnet build src/PerformanceTester.Orchestration`
Expected: Build succeeds.

#### Step 6: Commit

```bash
git add src/PerformanceTester.Orchestration/Phases/SetupPhase.cs \
        src/PerformanceTester.Orchestration/Phases/EventTestPhase.cs \
        src/PerformanceTester.Orchestration/Phases/ReportingPhase.cs \
        src/PerformanceTester.Orchestration/TestOrchestrator.cs
git commit -m "refactor(orchestration): consume named delegates instead of IEnumerable<IDockerMonitor>"
```

---

### Task 6: Update callers of AddOrchestration

**Files:**
- Modify: callers of `AddOrchestration` (find with `grep -rn "AddOrchestration" src/`)

Known callers:
- `src/PerformanceTester.Cli/Program.cs` (or wherever CLI wires DI)
- `src/PerformanceTester.Orchestration.IntegrationTests/Infrastructure/OrchestrationSystem.cs`

**Step 1: Find all callers**

Run: `grep -rn "AddOrchestration" src/`

**Step 2: Update each caller**

Each caller currently passes string container names (or uses defaults). Update to pass `NonEmptyString` value objects.

For CLI (`Program.cs` or similar):
```csharp
// Before (likely):
services.AddOrchestration(postgresConn, rabbitConn, rabbitMqContainerName, postgresContainerName);
// or with defaults:
services.AddOrchestration(postgresConn, rabbitConn);

// After:
services.AddOrchestration(
    postgresConn,
    rabbitConn,
    config.RabbitMqContainerName,
    config.PostgresContainerName);
```

For Orchestration integration tests:
```csharp
// Update to pass NonEmptyString values from test configuration
```

**Step 3: Verify full solution builds**

Run: `dotnet build`
Expected: Build succeeds across all projects except DockerMonitoring.IntegrationTests (updated in next task).

**Step 4: Commit**

```bash
git add -A  # Add all modified callers
git commit -m "refactor: update AddOrchestration callers to pass NonEmptyString container names"
```

---

### Task 7: Update DockerMonitoringSystem (test infrastructure)

**Files:**
- Modify: `src/PerformanceTester.DockerMonitoring.IntegrationTests/Infrastructure/DockerMonitoringSystem.cs`
- Modify: `src/PerformanceTester.DockerMonitoring.IntegrationTests/PerformanceTester.DockerMonitoring.IntegrationTests.csproj`

**Step 1: Add Infrastructure project reference to test .csproj**

The test project needs `NonEmptyString` for the container name parameters. Add:

```xml
    <ProjectReference Include="..\PerformanceTester.Infrastructure\PerformanceTester.Infrastructure.csproj" />
```

**Step 2: Rewrite DockerMonitoringSystem**

Replace the `DockerMonitoringSystem` class. Key changes:
- Expose `WarmupDockerMonitors`, `StartDockerMonitoring`, `GetDockerMetrics` delegates instead of `IDockerMonitor` instances
- Use the new `AddDockerMonitoring(params NonEmptyString[])` API
- Create `NonEmptyString` values for container names

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.IntegrationTesting.Logging;
using SystemBase = PerformanceTester.IntegrationTesting.System;

namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// System under test for Docker monitoring integration tests.
/// Exposes named delegates (not IDockerMonitor instances) per FP refactoring design.
/// </summary>
public sealed class DockerMonitoringSystem : SystemBase
{
    private IHost? _host;

    public static readonly NonEmptyString PostgresContainerName =
        NonEmptyString.Create<NonEmptyString>("performance-tester-postgres", "Postgres container", s => ???);
```

Wait — `NonEmptyString.Create<T>` has a `protected` constructor. The test needs to create `NonEmptyString` instances. But `NonEmptyString` is a base record with a `protected` constructor. We can't instantiate it directly.

Looking at the existing code: `RabbitMqContainerName.FromString("value")` creates instances. But the test project doesn't reference Orchestration (where those types live).

For tests, we need a way to create `NonEmptyString` values. Options:
1. Use `RabbitMqContainerName.FromString(...)` — requires referencing Orchestration
2. Create test-local `NonEmptyString` subtype
3. Use a simple wrapper

Actually, looking at `NonEmptyString` more carefully — it has `protected NonEmptyString(string value)`. We can create a simple concrete subclass in the test project:

```csharp
// Test-only concrete NonEmptyString for container names
private sealed record TestContainerName : NonEmptyString
{
    public TestContainerName(string value) : base(value) { }
}
```

Or even simpler — since `AddDockerMonitoring` accepts `params NonEmptyString[]`, and we just need any `NonEmptyString`, we can create a minimal concrete type.

Actually, let me reconsider. The `NonEmptyString` record's constructor is `protected`. To create one, you need a derived type. The simplest approach for tests:

```csharp
private sealed record ContainerName(string Value) : NonEmptyString(Value);
```

Wait, `NonEmptyString` constructor signature is `protected NonEmptyString(string value)` and it stores via `Value { get; }`. The derived record can't redeclare `Value`. Let me look at the exact signature again...

```csharp
public record NonEmptyString
{
    public string Value { get; }
    protected NonEmptyString(string value) => Value = value;
```

So a derived type would be:
```csharp
private sealed record TestContainerName : NonEmptyString
{
    public TestContainerName(string value) : base(value) { }
}
```

And usage:
```csharp
public static readonly NonEmptyString PostgresName = new TestContainerName("performance-tester-postgres");
public static readonly NonEmptyString RabbitMqName = new TestContainerName("performance-tester-rabbitmq");
```

This is simple and test-only. Include this as a nested private type inside `DockerMonitoringSystem`.

**Full rewrite of DockerMonitoringSystem.cs:**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.IntegrationTesting.Logging;
using SystemBase = PerformanceTester.IntegrationTesting.System;

namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// System under test for Docker monitoring integration tests.
/// Exposes named delegates (not IDockerMonitor instances) per FP refactoring design.
/// </summary>
public sealed class DockerMonitoringSystem : SystemBase
{
    private IHost? _host;

    /// <summary>Test-only concrete NonEmptyString for container names.</summary>
    private sealed record TestContainerName : NonEmptyString
    {
        public TestContainerName(string value) : base(value) { }
    }

    public static readonly NonEmptyString PostgresName = new TestContainerName("performance-tester-postgres");
    public static readonly NonEmptyString RabbitMqName = new TestContainerName("performance-tester-rabbitmq");

    public WarmupDockerMonitors WarmupDockerMonitors { get; private set; } = null!;
    public StartDockerMonitoring StartDockerMonitoring { get; private set; } = null!;
    public GetDockerMetrics GetDockerMetrics { get; private set; } = null!;

    /// <summary>
    /// Initializes Docker monitoring services for PostgreSQL and RabbitMQ containers.
    /// </summary>
    protected override async Task InitializeSystemAsync()
    {
        await base.InitializeSystemAsync();

        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        if (base.Output != null)
        {
            builder.Logging.AddXunitOutput(base.Output);
        }

        builder.Services.AddDockerMonitoring(PostgresName, RabbitMqName);

        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
        });

        _host = builder.Build();

        WarmupDockerMonitors = _host.Services.GetRequiredService<WarmupDockerMonitors>();
        StartDockerMonitoring = _host.Services.GetRequiredService<StartDockerMonitoring>();
        GetDockerMetrics = _host.Services.GetRequiredService<GetDockerMetrics>();
    }

    /// <summary>
    /// Starts all BackgroundServices and begins monitoring.
    /// </summary>
    public async Task StartMonitoringAsync(
        ReportDockerMonitorProgress? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (_host == null)
        {
            throw new InvalidOperationException("System not initialized");
        }

        await _host.StartAsync(cancellationToken);
        await StartDockerMonitoring(reportProgress ?? (_ => { }), cancellationToken);
    }

    /// <summary>
    /// Stops all BackgroundServices (ends monitoring).
    /// </summary>
    public async Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_host == null)
        {
            return;
        }

        await _host.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Disposes host and all services.
    /// </summary>
    public override void Dispose()
    {
        if (_host != null)
        {
            try
            {
                StopMonitoringAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore errors
            }

            _host.Dispose();
            _host = null;
        }

        base.Dispose();
    }
}
```

**Step 3: Commit**

```bash
git add src/PerformanceTester.DockerMonitoring.IntegrationTests/Infrastructure/DockerMonitoringSystem.cs \
        src/PerformanceTester.DockerMonitoring.IntegrationTests/PerformanceTester.DockerMonitoring.IntegrationTests.csproj
git commit -m "refactor(tests): update DockerMonitoringSystem to expose delegates"
```

---

### Task 8: Update test assertions and extensions

**Files:**
- Modify: `src/PerformanceTester.DockerMonitoring.IntegrationTests/Infrastructure/DockerMonitoringAssertions.cs`
- Modify: `src/PerformanceTester.DockerMonitoring.IntegrationTests/Infrastructure/DockerMonitorTestExtensions.cs`
- Modify: `src/PerformanceTester.DockerMonitoring.IntegrationTests/Infrastructure/DockerMonitorPhaseAwaiter.cs`

#### Step 1: Rewrite DockerMonitoringAssertions.cs

Assertions now operate on `GetDockerMetrics` delegate with a `NonEmptyString` container name parameter:

```csharp
using JoanComasFdz.AssertingThat;
using PerformanceTester.Infrastructure.ValueObjects;
using Xunit;

namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

public static class DockerMonitoringAssertions
{
    public static AssertingThat<GetDockerMetrics> HasCollectedMetricsFor(
        this AssertingThat<GetDockerMetrics> assertingThat,
        NonEmptyString containerName)
    {
        var metrics = assertingThat.InstanceToAssert(containerName);

        Assert.NotEmpty(metrics);

        return assertingThat;
    }

    public static AssertingThat<GetDockerMetrics> HasNotCollectedMetricsFor(
        this AssertingThat<GetDockerMetrics> assertingThat,
        NonEmptyString containerName)
    {
        var metrics = assertingThat.InstanceToAssert(containerName);

        Assert.Empty(metrics);

        return assertingThat;
    }

    public static AssertingThat<GetDockerMetrics> HasMinimumSampleCountFor(
        this AssertingThat<GetDockerMetrics> assertingThat,
        NonEmptyString containerName,
        int expectedMinimum)
    {
        var metrics = assertingThat.InstanceToAssert(containerName);

        Assert.True(
            metrics.Count >= expectedMinimum,
            $"{containerName} expected >= {expectedMinimum} samples, got {metrics.Count}");

        return assertingThat;
    }

    public static AssertingThat<GetDockerMetrics> HasValidCpuPercentagesFor(
        this AssertingThat<GetDockerMetrics> assertingThat,
        NonEmptyString containerName)
    {
        var metrics = assertingThat.InstanceToAssert(containerName);

        foreach (var metric in metrics)
        {
            Assert.True(
                metric.CpuPercent >= 0,
                $"{containerName} CPU% must be >= 0, got {metric.CpuPercent}");
            Assert.True(
                metric.CpuPercent <= 1000,
                $"{containerName} CPU% exceeds reasonable bound, got {metric.CpuPercent}");
        }

        return assertingThat;
    }

    public static AssertingThat<GetDockerMetrics> HasValidMemoryMeasurementsFor(
        this AssertingThat<GetDockerMetrics> assertingThat,
        NonEmptyString containerName)
    {
        var metrics = assertingThat.InstanceToAssert(containerName);

        foreach (var metric in metrics)
        {
            Assert.True(
                metric.MemoryMB > 0,
                $"{containerName} Memory must be > 0 MB, got {metric.MemoryMB}");
            Assert.True(
                metric.MemoryMB <= 100_000,
                $"{containerName} Memory exceeds reasonable bound, got {metric.MemoryMB} MB");
        }

        return assertingThat;
    }

    public static AssertingThat<GetDockerMetrics> HasMetricsInChronologicalOrderFor(
        this AssertingThat<GetDockerMetrics> assertingThat,
        NonEmptyString containerName)
    {
        var metrics = assertingThat.InstanceToAssert(containerName);
        var timestamps = metrics.Select(m => m.Timestamp).ToList();

        Assert.Equal(timestamps.OrderBy(t => t).ToList(), timestamps);

        return assertingThat;
    }
}
```

#### Step 2: Rewrite DockerMonitorTestExtensions.cs

The `WaitForSampleCountAsync` extension now operates on `GetDockerMetrics` delegate:

```csharp
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Test extensions for GetDockerMetrics delegate.
/// Provides convenient waiting mechanisms for sample collection.
/// </summary>
public static class DockerMonitorTestExtensions
{
    /// <summary>
    /// Waits until the specified container has collected at least the given number of samples.
    /// </summary>
    /// <param name="getDockerMetrics">The metrics retrieval delegate.</param>
    /// <param name="containerName">Container to check.</param>
    /// <param name="minimumCount">Minimum number of samples to wait for.</param>
    /// <param name="timeout">Timeout (default: 30 seconds).</param>
    /// <exception cref="TimeoutException">Thrown if timeout expires before reaching sample count.</exception>
    public static async Task WaitForSampleCountAsync(
        this GetDockerMetrics getDockerMetrics,
        NonEmptyString containerName,
        int minimumCount,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        try
        {
            while (getDockerMetrics(containerName).Count < minimumCount)
            {
                await Task.Delay(50, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            var actualCount = getDockerMetrics(containerName).Count;
            throw new TimeoutException(
                $"Timeout waiting for {minimumCount} samples from '{containerName}'. " +
                $"Only received {actualCount} samples after {effectiveTimeout.TotalSeconds}s.");
        }
    }
}
```

#### Step 3: Update DockerMonitorPhaseAwaiter.cs

Change from implementing `IProgress<DockerMonitorPhaseInfo>` to providing a `ReportDockerMonitorProgress`-compatible method:

```csharp
namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Helper class for tests to await specific Docker monitoring phase transitions.
/// Provides a ReportDockerMonitorProgress-compatible Report method and
/// TaskCompletionSource-based waiting for specific phases.
/// </summary>
public sealed class DockerMonitorPhaseAwaiter
{
    private readonly Dictionary<(string ContainerName, DockerMonitorPhase Phase, DockerMonitorPhaseState State), TaskCompletionSource> _phaseAwaiters = [];
    private readonly List<DockerMonitorPhaseInfo> _receivedPhases = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Gets all phase transitions received so far.
    /// </summary>
    public IReadOnlyList<DockerMonitorPhaseInfo> ReceivedPhases
    {
        get
        {
            lock (_lock)
            {
                return [.. _receivedPhases];
            }
        }
    }

    /// <summary>
    /// The progress reporting delegate. Pass this to StartDockerMonitoring.
    /// </summary>
    public ReportDockerMonitorProgress Report => OnProgressReport;

    /// <summary>
    /// Waits for a specific phase and state to be reported for the specified container.
    /// </summary>
    public async Task WaitForPhaseAsync(
        string containerName,
        DockerMonitorPhase phase,
        DockerMonitorPhaseState state,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        TaskCompletionSource tcs;
        var key = (containerName, phase, state);

        lock (_lock)
        {
            if (_receivedPhases.Any(p => p.ContainerName == containerName && p.Phase == phase && p.State == state))
            {
                return;
            }

            if (!_phaseAwaiters.TryGetValue(key, out tcs!))
            {
                tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _phaseAwaiters[key] = tcs;
            }
        }

        using var cts = new CancellationTokenSource(effectiveTimeout);
        using var registration = cts.Token.Register(() =>
            tcs.TrySetException(new TimeoutException(
                $"Timeout waiting for {containerName} to reach phase {phase}/{state} after {effectiveTimeout.TotalSeconds}s")));

        await tcs.Task;
    }

    /// <summary>
    /// Waits for a specific phase (Completed state) for the specified container.
    /// </summary>
    public Task WaitForPhaseAsync(
        string containerName,
        DockerMonitorPhase phase,
        TimeSpan? timeout = null)
        => WaitForPhaseAsync(containerName, phase, DockerMonitorPhaseState.Completed, timeout);

    private void OnProgressReport(DockerMonitorPhaseInfo value)
    {
        lock (_lock)
        {
            _receivedPhases.Add(value);

            var phaseKey = (value.ContainerName, value.Phase, value.State);
            if (_phaseAwaiters.TryGetValue(phaseKey, out var phaseTcs))
            {
                phaseTcs.TrySetResult();
                _phaseAwaiters.Remove(phaseKey);
            }
        }
    }
}
```

#### Step 4: Commit

```bash
git add src/PerformanceTester.DockerMonitoring.IntegrationTests/Infrastructure/DockerMonitoringAssertions.cs \
        src/PerformanceTester.DockerMonitoring.IntegrationTests/Infrastructure/DockerMonitorTestExtensions.cs \
        src/PerformanceTester.DockerMonitoring.IntegrationTests/Infrastructure/DockerMonitorPhaseAwaiter.cs
git commit -m "refactor(tests): update assertions and extensions for delegate-based API"
```

---

### Task 9: Update test methods

**Files:**
- Modify: `src/PerformanceTester.DockerMonitoring.IntegrationTests/DockerMonitorServiceTests.cs`

**Step 1: Rewrite all test methods**

All tests change from:
- `System.PostgresMonitor.WaitForSampleCountAsync(N)` → `System.GetDockerMetrics.WaitForSampleCountAsync(DockerMonitoringSystem.PostgresName, N)`
- `Asserting.That(System.PostgresMonitor).HasCollectedMetrics()` → `Asserting.That(System.GetDockerMetrics).HasCollectedMetricsFor(DockerMonitoringSystem.PostgresName)`
- `await System.StartMonitoringAsync(phaseAwaiter)` → `await System.StartMonitoringAsync(phaseAwaiter.Report)`

The "container not found" test (test 4) creates its own host — this test needs special handling since it creates a single-container setup:

```csharp
    [Fact]
    public async Task DockerMonitorService_ShouldHandleContainerNotFound_Gracefully()
    {
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();
        var nonexistentName = new TestContainerName("nonexistent-container");

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDockerMonitoring(nonexistentName);
        var host = builder.Build();

        var startDockerMonitoring = host.Services.GetRequiredService<StartDockerMonitoring>();
        var getDockerMetrics = host.Services.GetRequiredService<GetDockerMetrics>();

        await host.StartAsync();
        await startDockerMonitoring(phaseAwaiter.Report, CancellationToken.None);

        await phaseAwaiter.WaitForPhaseAsync(
            "nonexistent-container",
            DockerMonitorPhase.StreamFailed,
            DockerMonitorPhaseState.Failed);

        await host.StopAsync();

        phaseAwaiter.AssertPhaseReceived("nonexistent-container", DockerMonitorPhase.StreamFailed);
        Asserting.That(getDockerMetrics).HasNotCollectedMetricsFor(nonexistentName);
    }
```

Note: This test needs access to `TestContainerName` (the private nested type in `DockerMonitoringSystem`). Either:
- Make `TestContainerName` internal (and add InternalsVisibleTo), or
- Make it a standalone internal class in the test infrastructure namespace, or
- Move it to a shared location in the test project

Best: Make it a standalone `internal` class in the test infrastructure:

Create or add to a shared file:
```csharp
// In Infrastructure/TestContainerName.cs
namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

internal sealed record TestContainerName : NonEmptyString
{
    public TestContainerName(string value) : base(value) { }
}
```

Then use it in both `DockerMonitoringSystem` and the test class.

**Step 2: Full rewrite of DockerMonitorServiceTests.cs**

```csharp
using JoanComasFdz.AssertingThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace PerformanceTester.DockerMonitoring.IntegrationTests;

public sealed class DockerMonitorServiceTests : IntegrationTest
{
    private static readonly Infrastructure.NonEmptyString PostgresName = DockerMonitoringSystem.PostgresName;
    private static readonly Infrastructure.NonEmptyString RabbitMqName = DockerMonitoringSystem.RabbitMqName;

    public DockerMonitorServiceTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task StartMonitoringAsync_WhenCalled_ShouldStartBackgroundServices()
    {
        var phaseAwaiter = new DockerMonitorPhaseAwaiter();

        await System.StartMonitoringAsync(phaseAwaiter.Report);

        await phaseAwaiter.WaitForPhaseAsync(
            "performance-tester-postgres",
            DockerMonitorPhase.StreamConnected,
            DockerMonitorPhaseState.Completed);

        await System.GetDockerMetrics.WaitForSampleCountAsync(PostgresName, 1);
        await System.GetDockerMetrics.WaitForSampleCountAsync(RabbitMqName, 1);

        await System.StopMonitoringAsync();

        Asserting.That(System.GetDockerMetrics).HasCollectedMetricsFor(PostgresName);
        Asserting.That(System.GetDockerMetrics).HasCollectedMetricsFor(RabbitMqName);

        Output.WriteLine($"PostgreSQL: {System.GetDockerMetrics(PostgresName).Count} samples");
        Output.WriteLine($"RabbitMQ: {System.GetDockerMetrics(RabbitMqName).Count} samples");
    }

    // ... (all other tests follow the same pattern)
}
```

Write out ALL 9 test methods with the updated API. Use `static` aliases for `PostgresName` and `RabbitMqName` at the top for brevity.

**Step 3: Verify tests compile**

Run: `dotnet build src/PerformanceTester.DockerMonitoring.IntegrationTests`
Expected: Build succeeds.

**Step 4: Run tests**

Run: `dotnet test src/PerformanceTester.DockerMonitoring.IntegrationTests --logger "console;verbosity=detailed"`
Expected: All 9 tests pass.

**Step 5: Commit**

```bash
git add src/PerformanceTester.DockerMonitoring.IntegrationTests/
git commit -m "refactor(tests): update all DockerMonitoring tests for delegate-based API"
```

---

### Task 10: Full solution build and test verification

**Files:** None (verification only)

**Step 1: Build entire solution**

Run: `dotnet build`
Expected: Zero warnings, zero errors.

**Step 2: Run all tests**

Run: `dotnet test`
Expected: All tests pass.

**Step 3: Clean up any remaining IDockerMonitor references**

Run: `grep -rn "IDockerMonitor" src/`
Expected: No references remaining in `.cs` files. README.md files may still reference it — update those if found.

**Step 4: Update README files if needed**

If `src/PerformanceTester.DockerMonitoring/README.md` or `src/PerformanceTester.Orchestration/README.md` reference `IDockerMonitor`, update them to describe the new delegate-based API.

**Step 5: Final commit**

```bash
git add -A
git commit -m "docs: update READMEs for delegate-based DockerMonitoring API"
```

---

## Summary of Files Changed

| File | Action |
|------|--------|
| `DockerMonitoring/PerformanceTester.DockerMonitoring.csproj` | Add Infrastructure ref |
| `DockerMonitoring/DockerMonitoringDelegates.cs` | **Create** — 4 delegate definitions |
| `DockerMonitoring/DockerMonitorService.cs` | Use `ReportDockerMonitorProgress`, drop `IDockerMonitor` |
| `DockerMonitoring/ServiceCollectionExtensions.cs` | Full rewrite — `params NonEmptyString[]`, register delegates |
| `DockerMonitoring/IDockerMonitor.cs` | **Delete** |
| `Orchestration/ServiceCollectionExtensions.cs` | Single `AddDockerMonitoring` call, `NonEmptyString` params |
| `Orchestration/Phases/SetupPhase.cs` | Resolve `WarmupDockerMonitors` delegate |
| `Orchestration/Phases/EventTestPhase.cs` | Resolve `StartDockerMonitoring` delegate, add progress param |
| `Orchestration/Phases/ReportingPhase.cs` | Resolve `GetDockerMetrics` delegate |
| `Orchestration/TestOrchestrator.cs` | Clean up unused imports |
| Callers of `AddOrchestration` | Pass `NonEmptyString` container names |
| `DockerMonitoring.IntegrationTests/.csproj` | Add Infrastructure ref |
| `IntegrationTests/Infrastructure/DockerMonitoringSystem.cs` | Full rewrite — expose delegates |
| `IntegrationTests/Infrastructure/TestContainerName.cs` | **Create** — test-only NonEmptyString subclass |
| `IntegrationTests/Infrastructure/DockerMonitoringAssertions.cs` | Target `GetDockerMetrics` delegate |
| `IntegrationTests/Infrastructure/DockerMonitorTestExtensions.cs` | `WaitForSampleCountAsync` on delegate |
| `IntegrationTests/Infrastructure/DockerMonitorPhaseAwaiter.cs` | `ReportDockerMonitorProgress` delegate |
| `IntegrationTests/DockerMonitorServiceTests.cs` | All 9 tests updated |
