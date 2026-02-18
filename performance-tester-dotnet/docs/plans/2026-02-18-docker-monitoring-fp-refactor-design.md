# Docker Monitoring FP Refactor — Design

**Date:** 2026-02-18
**Status:** Approved

## Problem

`IDockerMonitor` is a multi-method OOP interface (Warmup, StartMonitoring, GetCollectedMetrics) consumed by three different Orchestration phases. Each phase uses only one method, yet all phases resolve `IEnumerable<IDockerMonitor>` and filter by container name. This leaks multi-container management into consumers and contradicts the codebase's FP direction (Guidelines 12, 14).

## Decision Summary

Replace `IDockerMonitor` with three named delegates. DockerMonitoring manages multiple containers internally and exposes only delegates. No consumer ever sees an interface or filters by container name.

## Design

### Dependencies Change

DockerMonitoring gains a project reference to `PerformanceTester.Infrastructure` for `NonEmptyString`. This allows the public API to accept `NonEmptyString` (the base type of `RabbitMqContainerName` and `PostgresContainerName`) without string unwrapping at call sites.

### Named Delegates (4 total)

Defined in the `PerformanceTester.DockerMonitoring` namespace, next to `DockerMetrics` and `DockerMonitorPhaseInfo`:

```csharp
/// Reports docker monitoring phase changes.
public delegate void ReportDockerMonitorProgress(DockerMonitorPhaseInfo phaseInfo);

/// Warms up Docker API for all registered containers.
public delegate Task WarmupDockerMonitors(CancellationToken ct = default);

/// Starts metrics collection on all registered containers.
/// Blocks until first sample is collected per container.
public delegate Task StartDockerMonitoring(
    ReportDockerMonitorProgress reportProgress,
    CancellationToken ct = default);

/// Retrieves collected metrics for a specific container.
public delegate IReadOnlyCollection<DockerMetrics> GetDockerMetrics(
    NonEmptyString containerName);
```

Key: `ReportDockerMonitorProgress` is non-nullable (always required). `WarmupDockerMonitors` and `StartDockerMonitoring` are plural (handle all containers). `GetDockerMetrics` is per-container via `NonEmptyString` parameter.

### DI Registration

Single registration call replaces per-container calls:

```csharp
public static IServiceCollection AddDockerMonitoring(
    this IServiceCollection services,
    params NonEmptyString[] containerNames)
{
    services.AddSingleton<DockerClientWrapper>();

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

    IEnumerable<DockerMonitorService> resolveMonitors(IServiceProvider sp) =>
        containerNames.Select(name =>
            sp.GetRequiredKeyedService<DockerMonitorService>(name.Value));

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
```

Call site in Orchestration:
```csharp
// Before:
services.AddDockerMonitoring(rabbitMqContainerName);
services.AddDockerMonitoring(postgresContainerName);

// After:
services.AddDockerMonitoring(
    configuration.RabbitMqContainerName,
    configuration.PostgresContainerName);
```

### IDockerMonitor Removal

`IDockerMonitor` is removed as a public interface. `DockerMonitorService` remains internal. Its methods (`WarmupAsync`, `StartMonitoringAsync`, `GetCollectedMetrics`) are the internal implementation that delegates call into.

### Orchestration Phase Changes

Phases resolve named delegates instead of `IEnumerable<IDockerMonitor>`:

**SetupPhase:**
```csharp
// Before:
var dockerMonitors = services.GetRequiredService<IEnumerable<IDockerMonitor>>();
WarmupDockerApi: () => Task.WhenAll(dockerMonitors.Select(m => m.WarmupAsync(ct)))

// After:
var warmupDockerMonitors = services.GetRequiredService<WarmupDockerMonitors>();
WarmupDockerApi: () => warmupDockerMonitors(ct)
```

**EventTestPhase:**
```csharp
// Before:
StartDockerMonitoring: () => Task.WhenAll(
    dockerMonitors.Select(m => m.StartMonitoringAsync(cancellationToken: ct)))

// After:
var startDockerMonitoring = services.GetRequiredService<StartDockerMonitoring>();
StartDockerMonitoring: (progress) => startDockerMonitoring(progress, ct)
```

**ReportingPhase:**
```csharp
// Before:
GetRabbitMqMetrics: () => dockerMonitors
    .Single(m => m.ContainerName == config.RabbitMqContainerName.Value)
    .GetCollectedMetrics()

// After:
var getDockerMetrics = services.GetRequiredService<GetDockerMetrics>();
GetRabbitMqMetrics: () => getDockerMetrics(config.RabbitMqContainerName)
GetPostgresMetrics: () => getDockerMetrics(config.PostgresContainerName)
```

### Test Impact

**Test System:** Exposes delegates instead of `IDockerMonitor` instances:
```csharp
// Before:
public IDockerMonitor PostgresMonitor { get; }
public IDockerMonitor RabbitMqMonitor { get; }

// After:
public WarmupDockerMonitors WarmupDockerMonitors { get; }
public StartDockerMonitoring StartDockerMonitoring { get; }
public GetDockerMetrics GetDockerMetrics { get; }
```

**Assertions:** Target `GetDockerMetrics` delegate instead of `IDockerMonitor`:
```csharp
// Before:
Asserting.That(System.PostgresMonitor).HasCollectedMetrics();

// After:
Asserting.That(System.GetDockerMetrics).HasCollectedMetricsFor(postgresName);
```

**Phase Awaiter:** Implements `ReportDockerMonitorProgress` delegate (instead of `IProgress<DockerMonitorPhaseInfo>`).

**Per-container error tests:** Register with a single container name for isolated failure testing.

## Guidelines Applied

| Guideline | Application |
|-----------|-------------|
| 12. Named delegates for single operations | Each IDockerMonitor method becomes its own delegate |
| 13. Named over Action/Func | `WarmupDockerMonitors`, `StartDockerMonitoring`, `GetDockerMetrics` |
| 14. Delegates at DI boundary | Delegates registered directly in DI, no interface |
| 20. Unwrap at boundaries | NonEmptyString accepted directly (no .Value needed at call site) |
| 29. Minimize interface reach | Phases see delegates, not IDockerMonitor interface |
