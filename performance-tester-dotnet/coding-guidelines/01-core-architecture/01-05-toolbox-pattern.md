# 01-05. Toolbox Pattern (Small Reusable Functions)

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
