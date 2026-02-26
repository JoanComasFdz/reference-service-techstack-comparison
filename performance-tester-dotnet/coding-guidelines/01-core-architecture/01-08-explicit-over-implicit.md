# 01-08. Explicit Over Implicit

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
