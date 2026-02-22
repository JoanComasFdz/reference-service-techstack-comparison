# Error Handling with Result Types

> Guidelines 15-17. For the full index and routing table, see [CODING_GUIDELINES.md](../CODING_GUIDELINES.md).

This codebase uses the `PerformanceTester.Functional` library (backed by [dunet](https://github.com/domn1995/dunet) discriminated unions) for typed error handling. These guidelines govern how Results are produced and consumed.

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
