# File Structure Convention: Public API vs Internal Implementation

**Date:** 2026-02-23
**Status:** Approved
**Scope:** Project-wide convention + Guideline 05-06 update

---

## Problem

When browsing a slice's file tree, you cannot tell which files form the public contract and which are internal implementation. This hurts both IDE navigation and conceptual understanding of the API surface.

## Convention

**One sentence:** Root files define the public contract; `Internal/` contains the implementation.

### Applicability

| Project type | Convention | Rationale |
|---|---|---|
| **Library projects** (consumed by other projects via `<ProjectReference>`) | `Api.cs` + `ServiceCollectionExtensions.cs` + optionally `ValueObjects/` + `Internal/` | Consumers need a clear public contract |
| **Terminal projects** (CLI, web host, workers — `<OutputType>Exe</OutputType>`) | Organize by domain concern, no `Api.cs` needed | No external consumers — everything is internal |

In this codebase: all Phase 1-4 slices are libraries (convention applies). `PerformanceTester.Cli` is terminal (convention does not apply).

### File Structure

```
{SliceName}/
├── Api.cs                               ← ALL public types (delegates, enums, records, utilities)
├── ServiceCollectionExtensions.cs       ← DI registration (public)
├── {SliceName}.csproj
├── ValueObjects/                        ← Optional: value objects (public, have validation/parsing logic)
│   └── SomeValueObject.cs
└── Internal/
    ├── {Concept}Module.cs               ← internal: context record + static operations + nested utilities
    └── {Concept}BackgroundService.cs    ← internal: thin shell (if applicable)
```

### What Goes Where

| Location | Contains | Visibility |
|---|---|---|
| `Api.cs` | Delegates, enums, record structs, data records, public utilities | `public` |
| `ServiceCollectionExtensions.cs` | DI registration (`Add{SliceName}()`) | `public` |
| `ValueObjects/` | Value objects with `Create()` factories, `FromXxx()` methods | `public` |
| `Internal/*.cs` | Module files, shell files, internal utilities | `internal` |

### Api.cs Reading Order

Following Guideline 02-05 (delegates → records → operations):

1. Delegates
2. Phase info (enums + record struct, if applicable)
3. Data records (output contracts)
4. Public utilities (static classes with logic)

### Internal/ Module Pattern

- Each conceptual subsystem gets one `{Concept}Module.cs` file
- Module is `internal static class` containing: context record, static operations, small utilities (nested)
- Shell is a separate `{Concept}BackgroundService.cs` file
- Only add subfolders inside `Internal/` when a slice has genuinely distinct subsystems

### Namespace Convention

- `PerformanceTester.{SliceName}` — public contract (`Api.cs`, `ServiceCollectionExtensions.cs`)
- `PerformanceTester.{SliceName}.ValueObjects` — value objects
- `PerformanceTester.{SliceName}.Internal` — implementation details

Consumers only ever `using PerformanceTester.{SliceName};`, never `.Internal`.

---

## Applied to ProcessMonitoring

```
ProcessMonitoring/
├── Api.cs                               ← delegates, phase info, ProcessMetrics, ProcessNameExtractor
├── ServiceCollectionExtensions.cs       ← AddProcessMonitoring()
├── PerformanceTester.ProcessMonitoring.csproj
├── ValueObjects/
│   └── SampleCount.cs                   ← NonNegativeInt value object
└── Internal/
    ├── ProcessMonitorModule.cs          ← MonitorContext, ProcessCpuCalculator, all static operations
    └── ProcessMonitorBackgroundService.cs ← thin shell
```

**Api.cs contents (~150-200 lines):**
- `ReportProcessMonitorProgressDelegate`
- `StartProcessMonitoringDelegate`
- `GetProcessMetricsDelegate`
- `ProcessMonitorPhase` enum
- `ProcessMonitorPhaseState` enum
- `ProcessMonitorPhaseInfo` record struct (with factory methods)
- `ProcessMetrics` record
- `ProcessNameExtractor` static class

**Internal/ProcessMonitorModule.cs:**
- `MonitorContext` (nested sealed record — mutable state container)
- `ProcessCpuCalculator` (nested sealed class — CPU delta calculator)
- `RunSamplingLoopAsync()` (public static — entry point for shell)
- `InitializeProcess()` (private static)
- `CollectSample()` (private static)
- `ReadCommandLine()` (private static)

**Internal/ProcessMonitorBackgroundService.cs:**
- Owns `MonitorContext` instance
- Wires `BackgroundService.ExecuteAsync` → `ProcessMonitorModule.RunSamplingLoopAsync`
- Exposes `StartMonitoringAsync` and `GetCollectedMetrics` (bound to delegates in DI)

## Applied to DockerMonitoring (Complex Slice)

```
DockerMonitoring/
├── Api.cs                               ← delegates, phase info, DockerMetrics
├── ServiceCollectionExtensions.cs       ← AddDockerMonitoring()
├── PerformanceTester.DockerMonitoring.csproj
├── ValueObjects/
│   ├── AttemptCount.cs
│   ├── ContainerId.cs
│   ├── CpuPercent.cs
│   ├── JitterMaxMilliseconds.cs
│   ├── MemoryMB.cs
│   ├── PostgresContainerName.cs
│   ├── RabbitMqContainerName.cs
│   └── SampleCount.cs
└── Internal/
    ├── MonitoringModule.cs              ← MonitorContext, monitoring operations, phase reporting
    ├── DockerMonitorBackgroundService.cs ← thin shell
    ├── ConnectionModule.cs              ← ConnectionState, ConnectionStateMachine, ReconnectionPolicy
    └── StatsModule.cs                   ← DockerOperations, StatsProcessing, internal delegates
```

Multiple modules in `Internal/` because DockerMonitoring has genuinely distinct subsystems. Structure stays flat unless subfolders are organically needed.

---

## Guideline 05-06 Update Required

The existing Guideline 05-06 (Thin Shell Pattern) needs updating to incorporate this file organization convention. Key changes:

1. Replace "no subdirectory" rule with the visibility-first structure above
2. Add `Api.cs` as a required file for library projects
3. Specify that `Internal/` replaces the previous flat-at-root approach for internal types
4. Add library vs terminal project scope distinction
5. Keep the module + shell pattern intact but locate them in `Internal/`
