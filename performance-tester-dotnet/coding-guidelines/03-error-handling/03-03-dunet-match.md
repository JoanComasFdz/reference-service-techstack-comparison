# 03-03. Use dunet `Match` for Exhaustive Result Consumption

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
