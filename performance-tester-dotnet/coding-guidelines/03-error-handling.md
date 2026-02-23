# Error Handling and Absence

> Guidelines 15-17, 36. For the full index and routing table, see [CODING_GUIDELINES.md](../CODING_GUIDELINES.md).

This codebase uses the `PerformanceTester.Functional` library (backed by [dunet](https://github.com/domn1995/dunet) discriminated unions) for typed error handling and domain absence. These guidelines govern how Results and Options are produced and consumed.

---

### 15. Use Result Types Instead of Exceptions for Expected Failures

Reserve exceptions for bugs and truly unexpected situations (out of memory, network down). For failures that are **part of the normal domain** (invalid input, resource not found, validation errors), return a `Result<TSuccess, TFailure>`.

```csharp
// ✅ Good - expected failure expressed in the return type
public static Result<TimeSpan, DurationParseError> Parse(string duration)
{
    if (string.IsNullOrWhiteSpace(duration))
        return new Failure(new DurationParseError.Empty());

    // ...
    return new Success(TimeSpan.FromSeconds(value));
}

// ❌ Avoid - exception for expected input validation
public static TimeSpan Parse(string duration)
{
    if (string.IsNullOrWhiteSpace(duration))
        throw new ArgumentException("Duration cannot be empty");

    // ...
    return TimeSpan.FromSeconds(value);
}
```

**Choosing `TFailure`:** Use a typed discriminated union (e.g., `DurationParseError`) when the caller needs to distinguish between different failure reasons. Use `string` when a human-readable message is sufficient.

**Result variant selection:**

|               | Simple failure (`string`) | Typed failure (`TFailure`) |
| ------------- | ------------------------- | -------------------------- |
| **Has value** | `Result<TValue>`          | `Result<TValue, TFailure>` |
| **Void**      | `Result<Unit>`            | `Result<Unit, TFailure>`   |

**Naming failure union types:** Use `{MethodAction}Error` — the name describes what failed, not where. Each variant carries contextual data. Define the union alongside the method that returns it.

```csharp
// ✅ Good - name describes the failed action, variants carry context
[Union]
public partial record ParseLineError
{
    public partial record EmptyInput;
    public partial record InvalidJson(string RawLine);
    public partial record IrrelevantMetric(string MetricName);
}

[Union]
public partial record ClearDatabaseError
{
    public partial record EmptyName;
    public partial record DatabaseNotFound(string Name);
    public partial record RetriesExhausted(int Attempts, Exception Last);
}

// ❌ Avoid - generic name, no context in variants
[Union]
public partial record AppError
{
    public partial record ValidationFailed;
    public partial record NotFound;
}
```

### 16. Use `using static` to Shorten Result Construction

Producer methods that return `Result<TSuccess, TFailure>` should add a `using static` directive to avoid repeating the full generic type on every `new Success(...)` / `new Failure(...)`.

```csharp
// ✅ Good - using static at the top of the file
using static PerformanceTester.Functional.Result<System.TimeSpan, DurationParseError>;

// Then in the method body:
return new Success(TimeSpan.FromSeconds(value));
return new Failure(new DurationParseError.Empty());

// ❌ Avoid - full type on every construction
return new Result<TimeSpan, DurationParseError>.Success(TimeSpan.FromSeconds(value));
return new Result<TimeSpan, DurationParseError>.Failure(new DurationParseError.Empty());
```

When `TSuccess` or `TFailure` uses types from other namespaces, use fully qualified names in the `using static` directive:

```csharp
using static PerformanceTester.Functional.Result<PerformanceTester.Functional.Unit, string>;
```

### 17. Use dunet `Match` for Exhaustive Result Consumption

When consuming a Result, always use dunet's generated `Match` method instead of native C# pattern matching (`is`, `is not`, `switch`). `Match` guarantees exhaustiveness at compile time — if a variant is added, all call sites fail to compile until updated.

**Extracting a value (or throwing on failure):**

```csharp
// ✅ Good - Match with exhaustive handling
var processId = serviceDiscoveryResult.Match(
    success: s => s.Value,
    failure: f => throw new TimeoutException($"Service not found: {f.Error}"));

// ❌ Avoid - native pattern matching (no compile-time exhaustiveness)
if (serviceDiscoveryResult is Result<int, string>.Success success)
    processId = success.Value;
else if (serviceDiscoveryResult is Result<int, string>.Failure failure)
    throw new TimeoutException($"Service not found: {failure.Error}");
```

**Side-effect on failure, no-op on success:**

```csharp
// ✅ Good - concise Match
(await _database.ClearDatabaseAsync(dbName, ct)).Match(
    success: _ => { },
    failure: f => throw new InvalidOperationException($"Failed to clear database: {f.Error}"));
```

**Boolean check:**

```csharp
// ✅ Good - Match to bool
public static bool IsValid(string duration) =>
    Parse(duration).Match(
        success: _ => true,
        failure: _ => false);

// ❌ Avoid - native pattern matching to bool
public static bool IsValid(string duration) =>
    Parse(duration) is Result<TimeSpan, DurationParseError>.Success;
```

**Processing with silent skip on failure:**

```csharp
// ✅ Good - Match with side-effects in success, empty failure
_metricsParser.ParseLine(line).Match(
    success: s =>
    {
        metrics.Add(s.Value);
        // ... process metric
    },
    failure: _ => { });
```

> **Exception for tests:** In unit tests, `Assert.IsType<Result<T, E>.Success>(result)` is acceptable because xUnit's type assertion provides sufficient exhaustiveness for test scenarios.

> **Exception for sequential pipelines:** In methods that chain multiple Result-returning operations and need to short-circuit on the first failure, use `IsFailure` + early return instead of `Match`. The `Match` lambda cannot `return` from the enclosing method, making it awkward for sequential composition.
>
> ```csharp
> // ✅ Good - sequential pipeline with early return
> var pidResult = await findServiceProcessId();
> if (pidResult.IsFailure)
>     return new Failure(pidResult.FailureError);
> var serviceProcessId = pidResult.SuccessValue;
>
> var dbResult = await clearDatabase();
> if (dbResult.IsFailure)
>     return new Failure($"Failed to clear database: {dbResult.FailureError}");
>
> // ... continue with more steps ...
> return new Success(serviceProcessId);
>
> // ❌ Avoid - Match in sequential pipeline (verbose, can't early-return)
> var pidResult = await findServiceProcessId();
> var pid = pidResult.Match(
>     success: s => (int?)s.Value,
>     failure: _ => null);
> if (pid is null)
>     return new Failure(pidResult.Match(success: _ => "", failure: f => f.Error));
> ```
>
> **Use `Match`** at consumption points (branching on outcome, extracting values).
> **Use `IsFailure` + early return** in sequential pipelines (checking and propagating).

---

### 36. Use `Option<T>` for Domain Absence, `T?` for Framework Interop

When a method legitimately produces "no value" — not a failure, just absence — use `Option<T>` from `PerformanceTester.Functional`. This forces callers to handle both cases via `Match`, preventing forgotten null checks. Use nullable `T?` only at framework/interop boundaries where .NET APIs return null.

**Decision table — which return type to use:**

| Situation | Return type | Example |
|-----------|-------------|---------|
| Operation failed with error details | `Result<T, TError>` (Guideline 15) | `ClearDatabase()` → `Result<Unit, ClearDatabaseError>` |
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

**`using static` for Option construction** follows the same pattern as Guideline 16 (Result):

```csharp
using static PerformanceTester.Functional.Option<PerformanceTester.DockerMonitoring.DockerMetrics>;
```

**Consuming `Option<T>`:** Use dunet `Match` at consumption points (branching on outcome, extracting values). Use `IsNone` / `IsSome` + early return (Guideline 11) in sequential pipelines, same split as `Result<T>` (Guideline 17).

```csharp
// ✅ Match — at consumption points
StatsProcessing.TryConvertToMetrics(stats, name, now).Match(
    some: s => ctx.CollectedMetrics.Add(s.Value),
    none: _ => { });

// ✅ IsNone + early return — in sequential pipelines (Guideline 11)
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
