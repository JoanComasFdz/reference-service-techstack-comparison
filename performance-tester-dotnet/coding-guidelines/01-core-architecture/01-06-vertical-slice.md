# 01-06. Vertical Slice Ownership

Each builder owns its complete rendering logic. Open the file → see everything it does. No need to navigate elsewhere.

```
// ✅ Good - self-contained vertical slice
ServiceMetricsPlotBuilder.cs  → All service plot logic here
RabbitMqMetricsPlotBuilder.cs → All RabbitMQ plot logic here

// ❌ Avoid - shared "smart" builder that requires navigation
ServiceMetricsPlotBuilder.cs  → Delegates to ResourcePlotBuilder
ResourcePlotBuilder.cs        → Actual logic hidden here
```
