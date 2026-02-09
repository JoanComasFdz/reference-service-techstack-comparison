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

## Error Handling with Result Types

This codebase uses the `JoanComasFdz.Result` library (backed by [dunet](https://github.com/domn1995/dunet) discriminated unions) for typed error handling. These guidelines govern how Results are produced and consumed.

### 12. Use Result Types Instead of Exceptions for Expected Failures

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

### 13. Use `using static` to Shorten Result Construction

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

### 14. Use dunet `Match` for Exhaustive Result Consumption

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

---

## Value Objects (Eliminating Primitive Obsession)

When a primitive (`int`, `string`, `TimeSpan`) has domain rules (valid range, format, non-empty), wrap it in a sealed record with a `Create()` factory returning `Result<T, TError>`. Once constructed, the value is guaranteed valid — "parse, don't validate."

### 15. Use Value Objects for Constrained Primitives

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

### 16. Value Object Structure

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

### 17. Unwrap `.Value` at Boundaries, Not Everywhere

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

### 18. No Unit Tests for Value Objects

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

---

## Summary

**One-liner:** *Make dependencies explicit, keep functions small and pure, let each file tell its own complete story.*

| Principle | Question to Ask |
|-----------|-----------------|
| Static classes | Does this class have instance state? If no → make it static |
| Explicit parameters | Can I see all inputs at the call site? |
| Inline single-use | Is this only used once? Inline it with a comment |
| Descriptive names | Does the name say exactly what it does? |
| Toolbox pattern | Is this function small, pure, and single-purpose? |
| Vertical slice | Can I understand this file without opening others? |
| Reasons for change | Will these things change together or separately? |
| Explicit over implicit | Do readers need to trace through indirection? |
| No useless wrappers | Does this wrapper add value? |
| Composition over interfaces | Do I actually need this abstraction? |
| Return early | Can I use a guard clause to avoid nesting? |
| Result over exceptions | Is this failure expected? Use Result, not exceptions |
| `using static` for Results | Am I producing Results? Shorten with `using static` |
| dunet Match | Am I consuming a Result? Use `Match`, not `is`/`switch` |
| Value objects | Does this primitive have domain constraints? Wrap it |
| Value object structure | sealed record, private ctor, `Create()` → Result, `ToString()` |
| Unwrap at boundaries | Am I crossing into a primitive-typed API? Use `.Value` |
| No VO unit tests | Is the validation trivially correct? Skip the test |
