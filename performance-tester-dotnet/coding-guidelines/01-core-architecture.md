# Core Architecture Principles

> Guidelines 01-01 through 01-11. For the full index and routing table, see [CODING_GUIDELINES.md](../CODING_GUIDELINES.md).

These are the foundational "how to structure code" principles. Read this when writing new classes, functions, or deciding how to organize code.

---

## Functional Architecture Principles

### 01-01. Static Classes for Pure Logic

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

### 01-02. Explicit Parameters Over Hidden State

Pass all dependencies as method parameters, not constructor injection. Makes data flow visible at the call site.

```csharp
// ✅ Good - all inputs explicit
public static Plot Build(ResourceMetricsReport? data, ChartConfig config)

// ❌ Avoid - hidden dependency
public Plot Build(ResourceMetricsReport? data)  // uses _config from field
```

### 01-03. Inline Single-Use Code

The real problem is a **vague name wrapping a trivial body**. A method called `IsValid` that contains `return value > TimeSpan.Zero` creates indirection without meaning — the expression already tells the full story, and the name tells you nothing the expression doesn't. In that case, inline it.

**When to inline:** the body is a trivial expression and the name adds no meaning beyond what the expression already communicates.

**When to keep a single-use method:** the name is specific and the body encapsulates a coherent multi-step operation whose steps would clutter the caller if inlined.

```csharp
// ❌ Avoid — vague name over a trivial body
if (IsValid(samplingInterval)) { ... }
private static bool IsValid(TimeSpan interval) => interval > TimeSpan.Zero;

// ✅ Option A — inline: the expression is already readable
if (samplingInterval > TimeSpan.Zero) { ... }

// ✅ Option B — rename: a specific name can justify extraction even for a one-liner
if (IsPositiveDuration(samplingInterval)) { ... }
private static bool IsPositiveDuration(TimeSpan interval) => interval > TimeSpan.Zero;
```

A method whose name precisely labels a multi-step operation is worth keeping even when called once:

```csharp
// ✅ Good — specific name, coherent multi-step body; inlining would clutter the caller
private static string? ReadCommandLine(int pid)
{
    var cmdLinePath = $"/proc/{pid}/cmdline";
    if (!File.Exists(cmdLinePath))
        return null;
    return File.ReadAllText(cmdLinePath).Replace('\0', ' ').Trim();
}

// ❌ Avoid — one-liner with a vague name; write it at the call site instead
private static void SetTickRotation(Plot plot)
{
    plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
}
```

The question to ask: **Does the name describe something the expression can't say at a glance?** If yes, keep the method. If no, inline or rename to something specific.

**Exception — Uniform factory-delegate groups:** When a group of private single-use methods all follow the same two-step body shape and all feed into one parent call site, extraction is justified even though no method is reused. Identify the pattern by checking all three properties:

1. **Uniform body shape** — every method in the group has the same structure: build a `deps` object, then return a delegate that closes over it.
2. **Name echoes property** — the method name mirrors the record property it configures (e.g., `BuildRunSetup` → `RunSetup:`), so the name acts as a label, not an abstraction.
3. **Bake-in content** — the returned delegate closes over captured variables (`deps`, `logger`, `ct`, `config`), meaning the body cannot be reduced to a single `=>` expression suitable for inlining.

When all three hold, the parent call site becomes a clean table of contents. Inlining would replace each labeled argument with a multi-line anonymous block, collapsing the table structure and making the wiring harder to scan.

```csharp
// ✅ Good — uniform factory-delegate group (N = 5, all same shape)
// Parent site reads as a table of contents:
return new Dependencies(
    RunSetup:     BuildRunSetup(services, clearDatabase, clearAllQueues, config, logger, ct),
    RunWarmup:    BuildRunWarmup(trackEvents, publishEvents, clearDatabase, clearAllQueues, config, logger, ct),
    RunEventTest: BuildRunEventTest(services, trackEvents, publishEvents, config, reportProgress, logger, ct),
    RunApiTest:   BuildRunApiTest(services, config, reportProgress, logger, ct),
    RunReporting: BuildRunReporting(services, config, logger, ct));

// Each body follows the identical two-step shape:
private static RunSetupDelegate BuildRunSetup(...)
{
    var deps = SetupPhaseModule.BuildDependencies(services, clearDatabase, clearAllQueues, config, ct);
    return (testRunId) => SetupPhaseModule.ExecuteAsync(testRunId, deps, logger);
}

// ❌ Avoid — inlining collapses the table into anonymous blocks that obscure structure:
return new Dependencies(
    RunSetup: (testRunId) =>
    {
        var deps = SetupPhaseModule.BuildDependencies(services, clearDatabase, clearAllQueues, config, ct);
        return SetupPhaseModule.ExecuteAsync(testRunId, deps, logger);
    },
    RunWarmup: () =>
    {
        var deps = WarmupPhaseModule.BuildDependencies(trackEvents, publishEvents, clearDatabase, clearAllQueues);
        return WarmupPhaseModule.ExecuteAsync(config, deps, logger, ct);
    },
    // ... 3 more blocks — table of contents is gone
    );
```

**Not the pattern** — a single isolated private method that does not belong to a uniform group of identically-shaped siblings. A one-off extraction should be inlined per the main rule.

### 01-04. Descriptive Function Names

Functions that configure should say **what** they configure. Avoid generic names that hide behavior.

```csharp
// ✅ Good - says exactly what it does
ConfigureLeftAxisLabel(plot, text, color, font)
AddScatterWithFill(plot, timestamps, values, color, lineWidth, fillAlpha, legendText)

// ❌ Avoid - vague names
ConfigureAxis(plot)
AddData(plot, data)
```

### 01-05. Toolbox Pattern (Small Reusable Functions)

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

### 01-06. Vertical Slice Ownership

Each builder owns its complete rendering logic. Open the file → see everything it does. No need to navigate elsewhere.

```
// ✅ Good - self-contained vertical slice
ServiceMetricsPlotBuilder.cs  → All service plot logic here
RabbitMqMetricsPlotBuilder.cs → All RabbitMQ plot logic here

// ❌ Avoid - shared "smart" builder that requires navigation
ServiceMetricsPlotBuilder.cs  → Delegates to ResourcePlotBuilder
ResourcePlotBuilder.cs        → Actual logic hidden here
```

### 01-07. Different Reasons for Change

If two things change for different reasons, they belong in different files. Even if code looks similar today, separate it if it has different futures.

```csharp
// ✅ Good - separate files for separate concerns
ServiceMetricsPlotBuilder.cs   // Might add thread count
RabbitMqMetricsPlotBuilder.cs  // Might add queue length
PostgresMetricsPlotBuilder.cs  // Might add connection count

// ❌ Avoid - single generic builder
ResourcePlotBuilder.cs         // Changes affect all plot types
```

### 01-08. Explicit Over Implicit

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

### 01-09. No Wrapper Functions for Clarity's Sake

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

**Exception — `BuildDependencies` in FP Module classes (Guideline 02-05):** A module's `BuildDependencies` factory (see [Delegates and Dependency Wiring, Guideline 02-05](02-delegates-and-dependency-wiring.md)) must exist even when it only forwards its parameters into `new Dependencies(...)` without any transformation. Its value is structural — it maintains the consistent four-part module reading contract (what I need → how to bundle → how to build → what I do with it) and ensures every module looks identical at a glance. A module whose particular phase happens to receive only pre-composed shared delegates is still a module. The trivial body is coincidental, not a signal to inline.

### 01-10. Composition Over Inheritance/Interfaces

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

### 01-11. Return Early to Avoid Nesting

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
