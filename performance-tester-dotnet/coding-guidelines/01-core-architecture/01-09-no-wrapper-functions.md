# 01-09. No Wrapper Functions for Clarity's Sake

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

**Exception — `BuildDependencies` in FP Module classes ([Guideline 02-05](../02-delegates-and-dependency-wiring/02-05-static-class-as-module.md)):** A module's `BuildDependencies` factory (see [Guideline 02-05](../02-delegates-and-dependency-wiring/02-05-static-class-as-module.md)) must exist even when it only forwards its parameters into `new Dependencies(...)` without any transformation. Its value is structural — it maintains the consistent four-part module reading contract (what I need → how to bundle → how to build → what I do with it) and ensures every module looks identical at a glance. A module whose particular phase happens to receive only pre-composed shared delegates is still a module. The trivial body is coincidental, not a signal to inline.
