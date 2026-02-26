# 03-04. Use `Option<T>` for Domain Absence, `T?` for Framework Interop

When a method legitimately produces "no value" — not a failure, just absence — use `Option<T>` from `PerformanceTester.Functional`. This forces callers to handle both cases via `Match`, preventing forgotten null checks. Use nullable `T?` only at framework/interop boundaries where .NET APIs return null.

**Decision table — which return type to use:**

| Situation | Return type | Example |
|-----------|-------------|---------|
| Operation failed with error details | `Result<T, TError>` (Guideline 03-01) | `ClearDatabase()` → `Result<Unit, ClearDatabaseError>` |
| Value may be absent (domain logic) | `Option<T>` | `TryConvertToMetrics()` → `Option<DockerMetrics>` |
| .NET API returns null | `T?` | `JsonSerializer.Deserialize<T>()` returns `T?` |

```csharp
// ✅ Good — domain absence expressed in return type
using static PerformanceTester.Functional.Option<DockerMetrics>;

public static Option<DockerMetrics> TryConvertToMetrics(
    ContainerStatsResponse stats,
    NonEmptyString containerName,
    DateTime timestamp)
{
    if (!HasValidPreCpuStats(stats))
    {
        return new None();
    }

    return new Some(new DockerMetrics
    {
        Timestamp = timestamp,
        ContainerId = ContainerId.FromString(stats.ID),
        ContainerName = containerName,
        CpuPercent = CpuPercent.FromDouble(CalculateCpuPercent(stats)),
        MemoryMB = MemoryMB.FromDouble(Math.Round(stats.MemoryStats.Usage / 1024.0 / 1024.0, 2))
    });
}

// Caller — forced to handle both cases
StatsProcessing.TryConvertToMetrics(stats, name, now).Match(
    some: s => ctx.CollectedMetrics.Add(s.Value),
    none: _ => { });

// ❌ Avoid — null for domain absence
public static DockerMetrics? TryConvertToMetrics(...)
{
    if (!HasValidPreCpuStats(stats))
    {
        return null;  // caller may forget to check
    }

    return new DockerMetrics { ... };
}
```

**`using static` for Option construction** follows the same pattern as Guideline 03-02 (Result):

```csharp
using static PerformanceTester.Functional.Option<PerformanceTester.DockerMonitoring.DockerMetrics>;
```

**Consuming `Option<T>`:** Use dunet `Match` at consumption points (branching on outcome, extracting values). Use `IsNone` / `IsSome` + early return ([Guideline 01-11](../01-core-architecture/01-11-return-early.md)) in sequential pipelines, same split as `Result<T>` ([Guideline 03-03](03-03-dunet-match.md)).

```csharp
// ✅ Match — at consumption points
StatsProcessing.TryConvertToMetrics(stats, name, now).Match(
    some: s => ctx.CollectedMetrics.Add(s.Value),
    none: _ => { });

// ✅ IsNone + early return — in sequential pipelines (Guideline 01-11)
var metricOption = StatsProcessing.TryConvertToMetrics(stats, name, now);
if (metricOption.IsNone)
{
    return;
}

var metric = metricOption.SomeValue;
```

**Use `Match`** when both branches produce a value or side-effect.
**Use `IsNone` / `IsSome` + early return** when you need to short-circuit the enclosing method.

**Acceptable — nullable at framework boundary:**

```csharp
// Framework returns null — wrapping in Option adds noise without safety
var report = JsonSerializer.Deserialize<ThroughputReport>(json);
if (report is null)
{
    logger.LogWarning("Failed to deserialize report");
    return null;  // fine — this IS the framework boundary
}
```

**When to use `Option<T>`:**

- The absence is a valid business state (no sample this interval, first Docker stats push has zeroed values)
- The caller must explicitly handle both cases
- The return type should communicate "this might not produce a value"

**When to use `T?`:**

- Framework APIs return null (`JsonSerializer`, `Process.GetProcessById`, file reads)
- The method is a thin wrapper around a nullable .NET API
- Private helpers where the calling code is within the same class and the null-coalescing pattern (`?? fallback`) is clear
