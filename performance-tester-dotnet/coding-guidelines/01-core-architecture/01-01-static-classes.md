# 01-01. Static Classes for Pure Logic

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
