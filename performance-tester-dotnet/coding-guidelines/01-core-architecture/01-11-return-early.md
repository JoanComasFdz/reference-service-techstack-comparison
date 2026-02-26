# 01-11. Return Early to Avoid Nesting

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
