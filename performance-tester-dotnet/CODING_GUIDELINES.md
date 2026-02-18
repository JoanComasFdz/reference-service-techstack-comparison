# Coding Guidelines

This document outlines the architectural and design principles used in this codebase, with a focus on functional programming patterns within C#/.NET 9.

---

## Functional Architecture Principles

### 1. Static Classes for Pure Logic

If a class has no instance state, make it `static`. This signals to developers: "these are pure functions, no instance needed."

```csharp
// ✅ Good - static class for pure functions
internal static class ServiceMetricsPlotBuilder
{
    public static Plot Build(ResourceMetricsReport? data, ChartConfig config) { ... }
}

// ❌ Avoid - instance class with no state
internal sealed class ServiceMetricsPlotBuilder
{
    private readonly ChartConfig _config;
    public ServiceMetricsPlotBuilder(ChartConfig config) => _config = config;
    public Plot Build(ResourceMetricsReport? data) { ... }
}
```

### 2. Explicit Parameters Over Hidden State

Pass all dependencies as method parameters, not constructor injection. Makes data flow visible at the call site.

```csharp
// ✅ Good - all inputs explicit
public static Plot Build(ResourceMetricsReport? data, ChartConfig config)

// ❌ Avoid - hidden dependency
public Plot Build(ResourceMetricsReport? data)  // uses _config from field
```

### 3. Inline Single-Use Code

If something is only used once, inline it with a descriptive comment. Don't wrap 2 lines in a function with a vague name.

```csharp
// ✅ Good - inline with comment explaining intent
private void ConfigureBottomPlot(Plot plot)
{
    PlotToolbox.ConfigureBottomAxisLabel(plot, _config.Font);

    // Rotate tick labels 45° for readability on bottom axis
    plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
    plot.Axes.Bottom.TickLabelStyle.Alignment = Alignment.MiddleLeft;
}

// ❌ Avoid - wrapper function that hides what it does
private void ConfigureBottomPlot(Plot plot)
{
    PlotToolbox.ConfigureBottomAxisLabel(plot, _config.Font);
    PlotToolbox.ConfigureTickLabelRotation(plot);  // What rotation? How?
}
```

### 4. Descriptive Function Names

Functions that configure should say **what** they configure. Avoid generic names that hide behavior.

```csharp
// ✅ Good - says exactly what it does
ConfigureLeftAxisLabel(plot, text, color, font)
AddScatterWithFill(plot, timestamps, values, color, lineWidth, fillAlpha, legendText)

// ❌ Avoid - vague names
ConfigureAxis(plot)
AddData(plot, data)
```

### 5. Toolbox Pattern (Small Reusable Functions)

Extract small, pure, single-purpose functions into a shared toolbox. Each function does ONE thing.

```csharp
// Toolbox of small, focused functions
internal static class PlotToolbox
{
    public static double[] ExtractTimestamps(IReadOnlyList<ResourceSampleJson> samples) => ...
    public static double[] ExtractCpuValues(IReadOnlyList<ResourceSampleJson> samples) => ...
    public static void AddScatterWithFill(Plot plot, double[] x, double[] y, ...) => ...
    public static void AddAverageLine(Plot plot, double value, Color color, ...) => ...
    public static void ConfigureLeftAxisLabel(Plot plot, string text, Color color, ...) => ...
}
```

Builders then compose these tools explicitly:

```csharp
internal static class ServiceMetricsPlotBuilder
{
    public static Plot Build(ResourceMetricsReport? data, ChartConfig config)
    {
        var plot = new Plot();
        var timestamps = PlotToolbox.ExtractTimestamps(data.Samples);

        PlotToolbox.AddScatterWithFill(plot, timestamps, PlotToolbox.ExtractCpuValues(data.Samples), ...);
        PlotToolbox.AddAverageLine(plot, data.CpuSummary.Avg, ...);
        // ... everything visible here

        return plot;
    }
}
```

### 6. Vertical Slice Ownership

Each builder owns its complete rendering logic. Open the file → see everything it does. No need to navigate elsewhere.

```
// ✅ Good - self-contained vertical slice
ServiceMetricsPlotBuilder.cs  → All service plot logic here
RabbitMqMetricsPlotBuilder.cs → All RabbitMQ plot logic here

// ❌ Avoid - shared "smart" builder that requires navigation
ServiceMetricsPlotBuilder.cs  → Delegates to ResourcePlotBuilder
ResourcePlotBuilder.cs        → Actual logic hidden here
```

### 7. Different Reasons for Change

If two things change for different reasons, they belong in different files. Even if code looks similar today, separate it if it has different futures.

```csharp
// ✅ Good - separate files for separate concerns
ServiceMetricsPlotBuilder.cs   // Might add thread count
RabbitMqMetricsPlotBuilder.cs  // Might add queue length
PostgresMetricsPlotBuilder.cs  // Might add connection count

// ❌ Avoid - single generic builder
ResourcePlotBuilder.cs         // Changes affect all plot types
```

### 8. Explicit Over Implicit

Prefer visible code over configuration-driven magic. Readers shouldn't have to trace through indirection.

```csharp
// ✅ Good - explicit at call site
PlotToolbox.AddScatterWithFill(
    plot, timestamps, values,
    color: ChartColors.ServiceCpu,      // Visible here
    lineWidth: config.Line.PrimaryLineWidth,
    fillAlpha: config.Line.PrimaryFillAlpha,
    legendText: "CPU %");

// ❌ Avoid - configuration object hides details
_resourceBuilder.Build(data, new ResourcePlotColors(
    ChartColors.ServiceCpu, ChartColors.ServiceCpuAvg,
    ChartColors.ServiceRam, ChartColors.ServiceRamAvg));  // What gets what?
```

### 9. No Wrapper Functions for Clarity's Sake

Don't create `DoThing()` just to wrap `library.DoThing()`. Only wrap when adding value (validation, defaults, or multi-step composition).

```csharp
// ✅ Good - wrapper adds value (combines multiple operations)
public static void ApplyStandardConfiguration(Plot plot, ChartConfig config, bool hasTitle = false)
{
    ConfigureGrid(plot, config.Grid);
    ConfigureLegend(plot, config.Font);
    ConfigureLayout(plot, config.Dimensions, hasTitle);
    ConfigureTimeAxis(plot);
}

// ❌ Avoid - wrapper adds no value
public static void SetRotation(Plot plot)
{
    plot.Axes.Bottom.TickLabelStyle.Rotation = 45;  // Just call this directly
}
```

### 10. Composition Over Inheritance/Interfaces

Don't create interfaces just for the sake of abstraction. Builders compose toolbox functions directly - flexibility without forced contracts.

```csharp
// ✅ Good - no interface, just composition
internal static class ServiceMetricsPlotBuilder { ... }
internal static class RabbitMqMetricsPlotBuilder { ... }

// ❌ Avoid - forced interface when not needed
internal interface IResourceMetricsPlotBuilder
{
    Plot Build(ResourceMetricsReport? data);
}
internal sealed class ServiceMetricsPlotBuilder : IResourceMetricsPlotBuilder { ... }
```

### 11. Return Early to Avoid Nesting

Use guard clauses and early returns to handle edge cases at the top of a method. This keeps the main logic at the base indentation level and avoids deeply nested `if`/`else` blocks.

```csharp
// ✅ Good - guard clauses flatten the method
public static Plot Build(ResourceMetricsReport? data, ChartConfig config)
{
    if (data is null)
        return CreateEmptyPlot(config);

    if (data.Samples.Count == 0)
        return CreateEmptyPlot(config);

    var plot = new Plot();
    var timestamps = PlotToolbox.ExtractTimestamps(data.Samples);
    PlotToolbox.AddScatterWithFill(plot, timestamps, ...);
    return plot;
}

// ❌ Avoid - nested conditions push main logic to the right
public static Plot Build(ResourceMetricsReport? data, ChartConfig config)
{
    if (data is not null)
    {
        if (data.Samples.Count > 0)
        {
            var plot = new Plot();
            var timestamps = PlotToolbox.ExtractTimestamps(data.Samples);
            PlotToolbox.AddScatterWithFill(plot, timestamps, ...);
            return plot;
        }
        else
        {
            return CreateEmptyPlot(config);
        }
    }
    else
    {
        return CreateEmptyPlot(config);
    }
}
```

---

## Function-Typed Dependencies (Named Delegates)

When Guideline 10 says "Composition Over Interfaces," the natural question is: _what replaces the interface?_ For single-operation dependencies, the answer is a **named delegate**. This section covers when to use delegates, how to name them, and how they coexist with interfaces at DI boundaries.

### 12. Use Named Delegates for Single-Operation Dependencies

When a dependency is a single operation (one method), use a named `delegate` instead of an interface. Lighter than an interface, more descriptive than raw `Action<T>`/`Func<T>`.

```csharp
// ✅ Good - named delegate for a single operation
public delegate void ReportApiLoadProgress(ApiLoadProgress apiLoadProgress);

// ✅ Good - named delegate with richer signature
public delegate Task<ApiLoadTestResult> StartApiLoadTest(
    string targetUrl,
    TimeSpan duration,
    int virtualUsers,
    ReportApiLoadProgress progress,
    int maxConsecutiveFailures,
    string? scriptDirectory);

// ❌ Avoid - single-method interface (ceremony without benefit)
public interface IApiLoadProgressReporter
{
    void Report(ApiLoadProgress progress);
}

// ❌ Avoid - raw Action<T> that loses semantic meaning
public static async Task ExecuteAsync(Action<ApiLoadProgress> progress) { ... }
```

**When to use:**

- The dependency is a single operation, not a family of related operations
- The caller only needs to supply one behavior

**When NOT to use:**

- The dependency has multiple related methods that change together → use an interface
- The dependency needs DI container registration at a slice boundary → use an interface (see Guideline 14)

### 13. Prefer Named Delegates Over `Action<T>` / `Func<T>`

A named delegate communicates intent at the type level. This extends Guideline 4 (Descriptive Names) to function-typed parameters.

```csharp
// ✅ Good - name says what it does
public delegate void ReportApiLoadProgress(ApiLoadProgress apiLoadProgress);
public delegate Task<Result<Unit, ClearDatabaseError>> ClearDatabase();
public delegate Task<Result<Unit, string>> ClearAllQueues();
public delegate Task<PublishMetrics> PublishEvents(int eventCount);

// ❌ Avoid - only says the shape, not the intent
Action<ApiLoadProgress>              // Could be anything that takes progress
Func<Task<Result<Unit, string>>>     // Could be any async operation
```

**Where to define the delegate:**

- **Next to its data type** if shared across callers (e.g., `ReportApiLoadProgress` next to `ApiLoadProgress` in `ApiLoadProgress.cs`)
- **Inside the consumer** if only used by one caller (e.g., `StartApiLoadTest` inside `ApiTestPhase`)

### 14. Interfaces at DI Boundaries, Delegates for Internal Wiring

Interfaces and delegates serve different layers. Use both, but in the right place:

| Layer                                        | Mechanism      | Example                                                              |
| -------------------------------------------- | -------------- | -------------------------------------------------------------------- |
| **DI boundary** (slice API)                  | Interface      | `IApiLoadTester`, `IRabbitMQ`                                        |
| **DI boundary** (single operation)           | Named delegate | `ClearDatabase`, `FindServiceProcessId`                              |
| **Internal wiring** (between static classes) | Named delegate | `StartApiLoadTest`                                                   |
| **Orchestrator**                             | Lambda adapter | Closes over `CancellationToken`, adapts interface/delegate → delegate |

```csharp
// ✅ Good - orchestrator adapts interface to delegate via lambda
var (apiResult, apiTestStartTime, apiTestEndTime) = await ApiTestPhase.ExecuteAsync(
    configuration,
    (url, duration, vus, apiProgress, maxFail, dir) =>
        _apiLoadTester.StartTestAsync(url, duration, vus, apiProgress, maxFail, dir, cancellationToken),
    progress,
    _logger);

// ✅ Good - shared delegates created once, reused across phases
PhasesToolbox.ClearDatabase clearDatabase = () =>
    clearDatabaseAsync(configuration.DatabaseName.Value, cancellationToken);

// ❌ Avoid - phase class depending directly on DI interface
public static async Task ExecuteAsync(IApiLoadTester apiLoadTester, ...) { ... }
// Couples the phase to the DI interface; the phase doesn't need the full interface
```

**Why the orchestrator adapts:**

- Phase classes stay decoupled from DI interfaces (testable with simple lambdas)
- `CancellationToken` belongs to the orchestrator, not the phase — the lambda closes over it
- The phase only sees the exact operation it needs, not the full interface surface

> **Evolution note:** Single-method interfaces at DI boundaries (e.g., the former `IDatabase`) are being migrated to named delegates as the functional approach extends beyond Orchestration. The interface-at-boundary rule applies primarily to multi-method contracts. For single-operation contracts, prefer a named delegate even at the DI boundary.

---

## Error Handling with Result Types

This codebase uses the `JoanComasFdz.Result` library (backed by [dunet](https://github.com/domn1995/dunet) discriminated unions) for typed error handling. These guidelines govern how Results are produced and consumed.

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
using static JoanComasFdz.Result.Result<System.TimeSpan, DurationParseError>;

// Then in the method body:
return new Success(TimeSpan.FromSeconds(value));
return new Failure(new DurationParseError.Empty());

// ❌ Avoid - full type on every construction
return new Result<TimeSpan, DurationParseError>.Success(TimeSpan.FromSeconds(value));
return new Result<TimeSpan, DurationParseError>.Failure(new DurationParseError.Empty());
```

When `TSuccess` or `TFailure` uses types from other namespaces, use fully qualified names in the `using static` directive:

```csharp
using static JoanComasFdz.Result.Result<JoanComasFdz.Result.Unit, string>;
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

## Value Objects (Eliminating Primitive Obsession)

When a primitive (`int`, `string`, `TimeSpan`) has domain rules (valid range, format, non-empty), wrap it in a sealed record with a `Create()` factory returning `Result<T, TError>`. Once constructed, the value is guaranteed valid — "parse, don't validate."

### 18. Use Value Objects for Constrained Primitives

```csharp
// ✅ Good - invalid state is unrepresentable
public sealed record EventCount
{
    public int Value { get; }
    private EventCount(int value) => Value = value;

    public static Result<EventCount, string> Create(int value) =>
        value is >= 1 and <= 1_000_000
            ? new Success(new EventCount(value))
            : new Failure($"Events must be between 1 and 1,000,000 (got: {value})");

    public override string ToString() => Value.ToString();
}

// ❌ Avoid - raw int with validation scattered across callers
public record TestConfiguration(int EventCount = 10000, ...);
// Then in ValidateOptions:
if (options.Events < 1 || options.Events > 1_000_000) return "error";
// Then in another caller: same check duplicated or forgotten
```

**When to use value objects:**

- The primitive has a valid range or format (e.g., 1–1,000,000)
- Multiple callers need to trust the value is valid
- The constraint is a domain rule, not a one-off check

**When NOT to use value objects:**

- The primitive is unconstrained (any `int` is fine)
- The constraint is only checked once at a single boundary
- The overhead outweighs the clarity (e.g., internal loop counters)

### 19. Value Object Structure

Follow this exact structure for consistency:

```csharp
using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<Namespace.ValueType, string>;

namespace PerformanceTester.Orchestration.ValueObjects;

// 1. Sealed record with private constructor
public sealed record EventCount
{
    public int Value { get; }
    private EventCount(int value) => Value = value;

    // 2. Static factory returning Result<T, string>
    public static Result<EventCount, string> Create(int value) =>
        value is >= 1 and <= 1_000_000
            ? new Success(new EventCount(value))
            : new Failure($"Events must be between 1 and 1,000,000 (got: {value})");

    // 3. ToString override for string interpolation
    public override string ToString() => Value.ToString();
}
```

**Key elements:**

- **`sealed record`** — immutable, value equality, cannot be subclassed
- **Private constructor** — forces callers through `Create()`
- **`Result<T, string>` for errors** — the error message lives next to the constraint, consumers just display it. Use a Dunet union error type only when callers need to branch on different failure kinds (e.g., `ClearDatabaseError` with `EmptyName`, `DatabaseNotFound`, `RetriesExhausted`)
- **`Create()` accepts the wider type** — e.g., `Create(int)` even if internal storage is `ushort`, to avoid casting noise at call sites
- **`ToString()` override** — enables seamless use in string interpolation and structured logging

### 20. Unwrap `.Value` at Boundaries, Not Everywhere

Downstream interfaces (e.g., `IEventPublisher.PublishEventsAsync(int count)`) still accept primitives. Unwrap `.Value` at the call site where the boundary is crossed.

```csharp
// ✅ Good - unwrap at the boundary
await _eventPublisher.PublishEventsAsync(config.EventCount.Value, cancellationToken);
NumEvents = config.EventCount.Value;  // assigning to int property
var rate = config.EventCount.Value / duration.TotalSeconds;  // arithmetic

// ✅ Good - no unwrap needed for string interpolation (ToString() handles it)
_logger.LogInformation("Processing {Count} events", config.EventCount);

// ❌ Avoid - unwrapping everywhere "just in case"
var count = config.EventCount.Value;
_logger.LogInformation("Processing {Count} events", count);
```

### 21. No Unit Tests for Value Objects

Value object validation logic (range checks, format checks) is trivially correct by inspection. The factory + Result pattern makes invalid construction impossible at compile time. Existing integration and validator tests exercise the parse path indirectly.

```csharp
// ✅ The factory IS the test — invalid values can't exist
EventCount.Create(0)       // → Failure("Events must be between 1 and 1,000,000 (got: 0)")
EventCount.Create(10000)   // → Success(EventCount(10000))
EventCount.Create(1000001) // → Failure("Events must be between 1 and 1,000,000 (got: 1000001)")

// ❌ Avoid - unit tests that restate the range check
[Fact] void Create_WithZero_ReturnsFailure() { ... }  // Just restating the condition
```

**Exception:** If a value object has complex parsing logic (regex, multi-step validation), tests may be warranted.

### 22. Value Object Families via Base Record

When multiple value objects share identical validation but represent distinct domain concepts, use a non-sealed base `record` with a `protected` constructor and a generic `Create<T>` factory. Derive sealed tag types that delegate to the base. This eliminates code duplication while providing compile-time swap prevention — you cannot accidentally pass a `RabbitMqContainerName` where a `PostgresContainerName` is expected.

```csharp
// ✅ Good - base record owns shared validation, derived types are tag types
public record ContainerName
{
    public string Value { get; }
    protected ContainerName(string value) => Value = value;

    public override string ToString() => Value;

    protected static Result<T, string> Create<T>(string value, string displayName, Func<string, T> factory)
        where T : ContainerName =>
        !string.IsNullOrWhiteSpace(value)
            ? new Result<T, string>.Success(factory(value.Trim()))
            : new Result<T, string>.Failure($"{displayName} container name cannot be empty");
}

public sealed record RabbitMqContainerName : ContainerName
{
    private RabbitMqContainerName(string value) : base(value) { }

    public static Result<RabbitMqContainerName, string> Create(string value) =>
        Create(value, "RabbitMQ", v => new RabbitMqContainerName(v));

    public static RabbitMqContainerName FromString(string value) => new(value);
}

public sealed record PostgresContainerName : ContainerName
{
    private PostgresContainerName(string value) : base(value) { }

    public static Result<PostgresContainerName, string> Create(string value) =>
        Create(value, "PostgreSQL", v => new PostgresContainerName(v));

    public static PostgresContainerName FromString(string value) => new(value);
}

// ❌ Avoid - single type with label parameter to distinguish at runtime
public sealed record ContainerName
{
    public static Result<ContainerName, string> Create(string value, string label) =>
        ...new Failure($"{label} container name cannot be empty");
}
// Call sites can swap arguments without compiler error:
var config = new TestConfiguration(
    RabbitMqContainerName: ContainerName.Create(postgresName, "RabbitMQ"),  // Bug! Wrong value, compiles fine
    PostgresContainerName: ContainerName.Create(rabbitMqName, "PostgreSQL"));
```

**When to use this pattern:**

- Two or more value objects have identical validation logic
- They appear as separate parameters in the same method/record (swap risk)
- The base validation can be parameterized (e.g., `displayName` for error messages)

**Key elements:**

- **Base `record`** (not `sealed`) — owns `Value`, `ToString()`, and `protected static Create<T>`
- **Derived `sealed record`** — tag type, private constructor, one-liner `Create()` delegates to base
- **`Create<T>` uses explicit `Result<T, string>` constructors** — cannot use `using static` because `T` is generic
- **`FromString()` on each derived type** — for test builders and known-valid paths

---

## Function Composition

### 23. Higher-Order Helper Functions for Structural Duplication

When the same structure (e.g., iterate + await all) is repeated across call sites with only the operation changing, extract the structure as a function that takes a function.

```csharp
// ❌ Structural duplication — same pattern, different operation
async () => { await Task.WhenAll(monitors.Select(m => m.WarmupAsync(ct))); }
async () => { await Task.WhenAll(monitors.Select(m => m.StartAsync(ct))); }

// ✅ Higher-order helper captures the repeated structure
Task forAllMonitors(Func<IMonitor, Task> action) => Task.WhenAll(monitors.Select(action));
() => forAllMonitors(m => m.WarmupAsync(ct))
() => forAllMonitors(m => m.StartAsync(ct))
```

**When to use:** Two or more call sites share identical structure but plug in different operations.

**When NOT to use:** Only one call site, or the structure is trivial (one-liner with no repetition).

### 24. Behavioral Decisions Belong in the Consumer, Not the Caller

When a class receives a shared function whose signature has a parameter it doesn't need, that class should accept the full signature and provide the default internally. The caller shouldn't encode knowledge about what the consumer does or doesn't care about.

```csharp
// ❌ Caller decides what the consumer needs — leaks knowledge outward
trackEvents: (count, timeout) => trackEvents(count, timeout, null),

// ✅ Consumer accepts the full signature and decides for itself
var task = trackEvents(count, timeout, progress: null);
```

**Why:** The consumer owns its behavior. If it later starts using the parameter, only the consumer changes — no caller needs updating.

---

## Formatting

These rules are enforced by `.editorconfig` where possible and by convention otherwise. Run `dotnet format` to auto-fix violations.

### 25. Always Use Braces in Control Flow Statements

Every `if`, `else`, `for`, `foreach`, `while`, `do`, and `using` must use braces, even when the body is a single line. This prevents bugs when lines are added later and makes the code structure unambiguous.

```csharp
// ✅ Good - braces always present
if (result.IsFailure)
{
    return Fail(TestPhase.Setup, result.FailureError);
}

foreach (var item in items)
{
    Process(item);
}

// ❌ Avoid - braceless single-line body
if (result.IsFailure)
    return Fail(TestPhase.Setup, result.FailureError);

foreach (var item in items)
    Process(item);
```

**Enforced by:** `.editorconfig` rule `csharp_prefer_braces = true:warning`

**No exceptions.** Even guard clauses and early returns use braces. The visual consistency outweighs the marginal brevity.

### 26. Blank Line After Closing Brace

Every closing brace `}` must be followed by a blank line, **except** when the next line is:

- Another closing brace `}`
- An `else`, `catch`, or `finally` keyword (continuation of the same statement)

This gives each block visual breathing room and makes the code scannable.

```csharp
// ✅ Good - blank line after each block
if (setupResult.IsFailure)
{
    return Fail(TestPhase.Setup, setupResult.FailureError);
}

var serviceProcessId = setupResult.SuccessValue;

try
{
    var warmupResult = await WarmupPhase.ExecuteAsync(...);

    if (warmupResult.IsFailure)
    {
        return Fail(TestPhase.Warmup, warmupResult.FailureError);
    }

    var warmupEndTime = DateTime.UtcNow;
}
catch (Exception ex)
{
    logger.LogError(ex, "Unexpected error");
}
finally
{
    await Cleanup();
}

// ❌ Avoid - no blank line after closing brace
if (setupResult.IsFailure)
{
    return Fail(TestPhase.Setup, setupResult.FailureError);
}
var serviceProcessId = setupResult.SuccessValue;
```

**Not enforced by `.editorconfig`** (no built-in rule). Enforced by convention and code review. Consider adding `StyleCop.Analyzers` (rule `SA1513`) if build-time enforcement is desired.

### 27. All-or-Nothing Parameter Wrapping

Parameters in method calls and declarations must be **all on one line** or **each on its own line**. Never group multiple parameters on a continuation line (partial wrap).

```csharp
// ✅ Good - all parameters on one line
var result = await TestReportLoader.LoadFromFolderAsync(folder, cancellationToken);

// ✅ Good - each parameter on its own line
var result = await SetupPhase.ExecuteAsync(
    testRunId,
    clearDatabase,
    findServiceProcessId,
    logger);

// ❌ Avoid - partial wrap (multiple params grouped on continuation line)
var result = await TestReportLoader.LoadFromFolderAsync(
    folder, cancellationToken);

// ❌ Avoid - partial wrap in logging
_logger.LogWarning("⚠️ Attempt {Attempt}/{Max} failed, retrying in {Delay}s...",
    attempt, MaxRetries, _retryDelay.TotalSeconds);

// ✅ Good - logging: all on one line if it fits
_logger.LogWarning("⚠️ Attempt {Attempt}/{Max} failed", attempt, MaxRetries);

// ✅ Good - logging: each arg on its own line if it doesn't fit
_logger.LogWarning(
    "⚠️ Attempt {Attempt}/{Max} failed, retrying in {Delay}s...",
    attempt,
    MaxRetries,
    _retryDelay.TotalSeconds);
```

**The rule:** If any parameter needs to wrap, **all** parameters wrap — one per line. This makes diffs cleaner (adding a parameter changes one line, not a reformatted group) and makes the call site scannable.

**Applies to:** Method calls, method declarations, constructor calls, delegate invocations, `new()` expressions, attribute parameters.

**Not enforced by `.editorconfig`** (no built-in rule). Enforced by convention and code review.

### 28. Expression Body (`=>`) Stays on the Same Line

When using expression-bodied members or lambda expressions, the expression after `=>` must start on the **same line** as the arrow. Never put a bare `=>` at the end of a line with the expression starting on the next line.

```csharp
// ✅ Good - expression on same line as =>
public override string ToString() => Value.ToString();

// ✅ Good - short lambda on same line
var names = items.Select(x => x.Name);

// ✅ Good - multi-param declaration with each param on its own line, expression on => line
public static Result<EventCount, string> Create(
    int value,
    int maxValue) => value is >= 1 and <= maxValue
        ? new Success(new EventCount(value))
        : new Failure($"Invalid (got: {value})");

// ✅ Good - when the expression is complex, open a block body instead
public static Result<EventCount, string> Create(int value)
{
    if (value is < 1 or > 1_000_000)
    {
        return new Failure($"Events must be between 1 and 1,000,000 (got: {value})");
    }

    return new Success(new EventCount(value));
}

// ❌ Avoid - newline right after =>
public override string ToString()
    => Value.ToString();

// ❌ Avoid - bare => at end of line
public static OrchestratorDeps Build(IServiceProvider services, Config config) =>
    new(
        RunSetup: ...,
        RunProcess: ...);
```

**Why:** The expression after `=>` is the most important part — it's _what the function does_. Pushing it to the next line hides it. If the expression is too long for one line, switch to a block body `{ }` instead of dangling the arrow.

**Not enforced by `.editorconfig`** (no built-in rule). Enforced by convention and code review.

---

## 29. Minimize Interface Reach with Dependency Composition

Classes should depend on **pre-composed capabilities**, not on the interfaces or individual operations behind them. Interfaces are a DI registration concern — contain them in a **dependencies class**: a static factory that resolves interfaces and produces bound delegates at the right abstraction level. Consumers receive delegates matching their actual abstraction level.

```csharp
// ✅ Good — dependencies class composes phases, orchestrator sees only phase delegates
internal static class OrchestratorDependencies
{
    public static OrchestratorDeps Build(
        IServiceProvider services, Config config, CancellationToken ct) => new(
        RunSetup: (testRunId) => SetupPhase.ExecuteAsync(testRunId,
            clearDb: () => services.GetRequiredService<IDatabase>().ClearAsync(config.Db, ct),
            ...),
        RunProcess: () => ProcessPhase.ExecuteAsync(
            start: () => services.GetRequiredService<IProcessRunner>().StartAsync(ct),
            ...));
}

// Consumer — knows only about phases, not their internals
internal static class Orchestrator
{
    public static async Task<Result<Report, Error>> RunAsync(OrchestratorDeps deps)
    {
        var setup = await deps.RunSetup(Guid.NewGuid());
        var result = await deps.RunProcess();
        ...
    }
}

// ❌ Avoid — consumer depends on every individual operation
public class Orchestrator(IDatabase db, IProcessRunner runner, IEventPublisher publisher)
{
    // Knows about clearing databases, starting processes, publishing events...
    // Should only know about phases.
}
```

**Why:**

- Classes should know about their dependencies at their abstraction level, not every small operation
- Dependency composition classes centralize plumbing (interface resolution, config binding, CT binding)
- Consumers become testable with simple lambdas at the right granularity
- Adding a new operation inside a phase doesn't change the orchestrator

**The pattern: Configure → Build → Run**

1. **Configure:** Register interfaces in DI (`AddInfrastructure()`, `AddEventPublishing()`, etc.)
2. **Build:** Dependencies class resolves interfaces, composes them into capability-level delegates
3. **Run:** Consumer calls delegates with zero knowledge of interfaces or internal operations

**When to use:**

- Any class that coordinates multiple components (orchestrators, pipelines)
- Any class where you use only specific methods from broader interfaces
- Any static class that needs "injected" capabilities

**When NOT to use:**

- Inside the dependencies class itself (it must see interfaces to compose them)
- Leaf classes that genuinely work at the operation level (the phases themselves)

### 30. Static Class as Module (Co-located Dependencies)

A static class can serve as an **FP-style module** — owning its delegate definitions, a `Dependencies` record that bundles them, a factory to build them, and the execution method. This mirrors FP companion modules (e.g., F# allows a type and module to share the same name). In C#, the static class **is** the module — no separate builder class needed.

This improves discoverability, especially for delegates where IDE Ctrl+Click navigation doesn't work. Everything reads top-to-bottom: **what I need → how to bundle it → how to build it → what I do with it**.

```csharp
// ✅ Good - self-contained module: types → bundle → factory → execution
internal static class SetupPhase
{
    // 1. Delegate definitions (what I need)
    public delegate Task<Result<int, string>> FindServiceProcessId();
    public delegate bool IsMonitoringStarted();
    public delegate Task StartMonitoring();
    public delegate Task WarmupDockerApi();
    public delegate Task ConnectEventPublisher();

    // 2. Dependencies record (bundle of what I need)
    public record Dependencies(
        FindServiceProcessId FindServiceProcessId,
        IsMonitoringStarted IsMonitoringStarted,
        StartMonitoring StartMonitoring,
        WarmupDockerApi WarmupDockerApi,
        PhasesToolbox.ClearDatabase ClearDatabase,
        PhasesToolbox.ClearAllQueues ClearAllQueues,
        ConnectEventPublisher ConnectEventPublisher);

    // 3. Factory (how to build what I need from DI)
    public static Dependencies BuildDependencies(
        IServiceProvider services,
        PhasesToolbox.ClearDatabase clearDatabase,
        PhasesToolbox.ClearAllQueues clearAllQueues,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var findServiceProcessId = services.GetRequiredService<FindServiceProcessId>();
        // ... resolve and compose ...
        return new Dependencies(...);
    }

    // 4. Execution (what I do with it)
    public static async Task<Result<int, string>> ExecuteAsync(
        Guid testRunId,
        Dependencies deps,
        ILogger logger)
    {
        var pidResult = await deps.FindServiceProcessId();
        // ...
    }
}

// ❌ Avoid - separate file/class just for building dependencies
internal static class SetupPhaseDependencies
{
    public static RunSetup Build(IServiceProvider services, ...) { ... }
}
```

**Why:** In FP languages, a module contains both its types and its functions — there's no separate "builder" concept. C# static classes serve the same role. Co-locating definitions, bundling, construction, and execution gives a top-to-bottom reading flow and eliminates file-hopping.

**When to use:**

- A static method has many delegate parameters (4+) that are always passed together
- The delegates are only used by this one consumer
- IDE navigation for delegates is important (no Ctrl+Click on delegate types)

**When NOT to use:**

- The factory needs to be called from multiple unrelated sites (keep it separate)
- The dependencies are shared across multiple consumers (use `PhasesToolbox` instead)

---

## Lambda Binding Strategy

### 31. Three-Bucket Rule for Lambda Binding

When composing delegates (lambdas) in a dependencies class or factory, every value falls into one of three buckets based on its **lifetime**. Choosing the wrong bucket leads to either stale closures or unnecessary parameters.

| Bucket                | Lifetime                                | Mechanism                          | Example                                                                 |
| --------------------- | --------------------------------------- | ---------------------------------- | ----------------------------------------------------------------------- |
| **Bake in**           | Immutable for the app's lifetime        | Close over in the lambda           | Config values, connection strings, container names, `CancellationToken` |
| **Pass as parameter** | Produced at runtime, different per call | Lambda parameter                   | `testRunId`, `serviceProcessId`, `testResult`                           |
| **Reader delegate**   | Can change during the app's lifetime    | `Func<T>` that reads current value | Feature flags, user preferences, dynamic settings                       |

**Bake in** — the value is known at construction time and will never change:

```csharp
// ✅ Good - DatabaseName and CancellationToken are fixed for the app's lifetime
PhasesToolbox.ClearDatabase clearDatabase = () => database.ClearDatabaseAsync(config.DatabaseName.Value, ct);

// ✅ Good - container name won't change mid-run
GetRabbitMqMetrics: () => dockerMonitor.GetMetricsAsync(config.RabbitMqContainerName.Value),
```

**Pass as parameter** — the value is produced during execution and varies per invocation:

```csharp
// ✅ Good - testRunId is generated at runtime, serviceProcessId comes from a previous phase
public delegate Task<Result<ProcessId, string>> RunSetup(Guid testRunId);
public delegate Task<Result<EventTestOutput, string>> RunEventTest(ProcessId serviceProcessId);

// ❌ Avoid - baking in a runtime value that doesn't exist yet
// (This would require constructing the delegate after the value is produced,
//  breaking the Configure → Build → Run separation)
public static async Task ExecuteAsync(FindServiceProcessId findServiceProcessId)
{
    var pid = await findServiceProcessId();
    var deps = EventTestPhase.BuildDependencies(pid, ...);  // pid baked into delegates
    await EventTestPhase.ExecuteAsync(deps);                 // too late — Build already ran
}
```

**Reader delegate** — the value may change between calls:

```csharp
// ✅ Good - if a setting could be toggled at runtime, read it each time
public delegate int GetMaxRetries();  // reads current value on each call

var deps = new Dependencies(
    GetMaxRetries: () => settingsStore.CurrentMaxRetries,
    ...);

// ❌ Avoid - baking in a mutable value (stale closure)
var maxRetries = settingsStore.CurrentMaxRetries;
var deps = new Dependencies(
    MaxRetries: maxRetries,  // snapshot — won't reflect later changes
    ...);
```

**Decision flowchart:**

1. **Does the value exist at construction time?**
    - No → it's a **lambda parameter** (produced at runtime)
    - Yes → continue to 2
2. **Can the value change after construction?**
    - Yes → use a **reader delegate** (`Func<T>` or named delegate)
    - No → **bake it in** (close over it)

**Current codebase examples:**

| Value                          | Bucket    | Where                                                                           |
| ------------------------------ | --------- | ------------------------------------------------------------------------------- |
| `config.DatabaseName`          | Bake in   | `TestOrchestrator.BuildDependencies` — closed over in `ClearDatabase` lambda    |
| `config.RabbitMqContainerName` | Bake in   | `ReportingPhase.BuildDependencies` — closed over in `GetRabbitMqMetrics` lambda |
| `CancellationToken`            | Bake in   | All phase delegates — closed over at construction                               |
| `testRunId`                    | Parameter | `RunSetup(Guid testRunId)` — generated at runtime                               |
| `serviceProcessId`             | Parameter | `RunEventTest(ProcessId serviceProcessId)` — output of Setup phase              |
| `testResult`                   | Parameter | `RunReporting(TestResult testResult)` — assembled from all phase outputs        |

**Why this matters:**

- **Baking in a runtime value** forces you to delay delegate construction, breaking the clean Configure → Build → Run separation (Guideline 29)
- **Passing a fixed value as a parameter** clutters every call site with values that never change
- **Baking in a mutable value** creates stale closures that silently use outdated data
- **Using a reader delegate for an immutable value** adds unnecessary indirection

---

## Summary

**One-liner:** _Make dependencies explicit, keep functions small and pure, let each file tell its own complete story._

| Principle                   | Question to Ask                                                                                               |
| --------------------------- | ------------------------------------------------------------------------------------------------------------- |
| Static classes              | Does this class have instance state? If no → make it static                                                   |
| Explicit parameters         | Can I see all inputs at the call site?                                                                        |
| Inline single-use           | Is this only used once? Inline it with a comment                                                              |
| Descriptive names           | Does the name say exactly what it does?                                                                       |
| Toolbox pattern             | Is this function small, pure, and single-purpose?                                                             |
| Vertical slice              | Can I understand this file without opening others?                                                            |
| Reasons for change          | Will these things change together or separately?                                                              |
| Explicit over implicit      | Do readers need to trace through indirection?                                                                 |
| No useless wrappers         | Does this wrapper add value?                                                                                  |
| Composition over interfaces | Do I actually need this abstraction?                                                                          |
| Return early                | Can I use a guard clause to avoid nesting?                                                                    |
| Named delegates             | Is this dependency a single operation? Use a named delegate                                                   |
| Named over Action/Func      | Does the delegate name describe what it does?                                                                 |
| Interfaces vs delegates     | Am I at a DI boundary (interface) or internal wiring (delegate)?                                              |
| Result over exceptions      | Is this failure expected? Use Result, not exceptions                                                          |
| `using static` for Results  | Am I producing Results? Shorten with `using static`                                                           |
| dunet Match                 | Am I consuming a Result? Use `Match` (consumption) or `IsFailure` (pipelines)                                 |
| Value objects               | Does this primitive have domain constraints? Wrap it                                                          |
| Value object structure      | sealed record, private ctor, `Create()` → Result, `ToString()`                                                |
| Unwrap at boundaries        | Am I crossing into a primitive-typed API? Use `.Value`                                                        |
| No VO unit tests            | Is the validation trivially correct? Skip the test                                                            |
| Value object families       | Do multiple VOs share the same validation? Base record + sealed tag types                                     |
| Higher-order helpers        | Is the same structure repeated with different operations plugged in?                                          |
| Consumer owns defaults      | Am I encoding what a consumer needs? Let the consumer decide                                                  |
| Always use braces           | Does every `if`/`else`/`for`/`while`/`using` have braces?                                                     |
| Blank line after `}`        | Is there a blank line after every closing brace (unless followed by another `}`, `else`, `catch`, `finally`)? |
| All-or-nothing params       | Are parameters all on one line, or each on its own line? Never partial wrap                                   |
| `=>` same line              | Does the expression start on the same line as `=>`? If too long, use block body                               |
| Dependency composition      | Am I receiving interfaces? Contain them in a dependencies class, expose delegates at the right level          |
| Static class as module      | Can I co-locate delegates, bundle record, factory, and execution in one static class?                         |
| Three-bucket rule           | Is this value fixed at construction, produced at runtime, or mutable? Bake in / parameter / reader delegate   |
