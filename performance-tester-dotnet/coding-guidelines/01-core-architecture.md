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
