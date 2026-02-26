# 01-10. Composition Over Inheritance/Interfaces

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
