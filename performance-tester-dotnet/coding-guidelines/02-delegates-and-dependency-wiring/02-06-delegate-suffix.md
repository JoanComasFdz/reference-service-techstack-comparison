# 02-06. Delegate Suffix Convention

All named delegate types must end with `Delegate`. This makes delegate types instantly recognizable as function types — distinct from classes, interfaces, and methods.

```csharp
// ✅ Good - "Delegate" suffix identifies these as function types
internal delegate Task<Result<string, DockerError>> GetContainerIdDelegate(
    string containerName,
    CancellationToken ct = default);

internal delegate IAsyncEnumerable<DockerMetrics> StreamMetricsDelegate(
    string containerId,
    NonEmptyString containerName,
    CancellationToken ct);

public delegate Task<Result<Unit, ClearDatabaseError>> ClearDatabaseDelegate(
    CancellationToken cancellationToken = default);

// ❌ Avoid - no suffix, ambiguous type
public delegate Task<Result<Unit, ClearDatabaseError>> ClearDatabase(
    CancellationToken cancellationToken = default);

public delegate Task<Result<ProcessId, string>> FindServiceProcessId(
    Port port,
    CancellationToken cancellationToken);
```

**Why the suffix matters:**

The suffix solves three problems:

1. **Type vs parameter ambiguity in records.** Without the suffix, positional record parameters repeat the type name verbatim — `RunSetup RunSetup`. Readers must check context to know which is the type and which is the parameter. The suffix makes it instant:

```csharp
// ❌ Avoid - type and parameter names are identical
public record Dependencies(
    RunSetup RunSetup,
    RunWarmup RunWarmup,
    FindServiceProcessId FindServiceProcessId,
    ClearDatabase ClearDatabase);

// ✅ Good - type is clearly distinguished from parameter
public record Dependencies(
    RunSetupDelegate RunSetup,
    RunWarmupDelegate RunWarmup,
    FindServiceProcessIdDelegate FindServiceProcessId,
    ClearDatabaseDelegate ClearDatabase);
```

2. **Readability at usage sites.** When scanning code, `ClearDatabaseDelegate clearDatabase` immediately communicates "this is a function I can call." Without the suffix, `ClearDatabase clearDatabase` looks like a constructor call or variable declaration of a class instance.

3. **IDE discoverability.** Searching for "Delegate" surfaces all function types. Without the suffix, delegate types are mixed in with classes and interfaces in search results.

**Naming the parameter:** The parameter name drops the suffix and uses camelCase — the suffix is on the TYPE, not the variable:

```csharp
// ✅ Good - type has suffix, parameter does not
public static async Task ExecuteAsync(
    ClearDatabaseDelegate clearDatabase,
    FindServiceProcessIdDelegate findServiceProcessId,
    ILogger logger)
{
    var result = await clearDatabase(ct);
    var pid = await findServiceProcessId(port, ct);
}

// ❌ Avoid - suffix on parameter name (redundant noise)
public static async Task ExecuteAsync(
    ClearDatabaseDelegate clearDatabaseDelegate,
    FindServiceProcessIdDelegate findServiceProcessIdDelegate,
    ILogger logger)
```

> **Evolution note:** All delegate declarations across the codebase have been migrated to use the `Delegate` suffix (e.g., `ClearDatabaseDelegate`, `RunSetupDelegate`). Examples in earlier guidelines (02-01, 02-02, 02-03, 02-05, 05-03) may still show unsuffixed names for brevity; the production code is the authoritative reference.

**Applies to:** All `delegate` type declarations — `public`, `internal`, and `private`. No exceptions.

**Relationship to other guidelines:**

- Constrains **[Guideline 02-01](02-01-named-delegates.md)** (named delegates) — [Guideline 02-01](02-01-named-delegates.md) says _when_ to use delegates; this says _how to name_ them
- Extends **[Guideline 02-02](02-02-named-over-action-func.md)** (named over Action/Func) — [Guideline 02-02](02-02-named-over-action-func.md) says use a descriptive name; the `Delegate` suffix is part of that name
- Affects **[Guideline 02-05](02-05-static-class-as-module.md)** (static class as module) — Dependencies records benefit most from the suffix (type vs parameter disambiguation)
