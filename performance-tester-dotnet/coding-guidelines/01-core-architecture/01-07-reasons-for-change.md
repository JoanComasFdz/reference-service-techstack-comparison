# 01-07. Different Reasons for Change

If two things change for different reasons, they belong in different files. Even if code looks similar today, separate it if it has different futures.

```csharp
// ✅ Good - separate files for separate concerns
ServiceMetricsPlotBuilder.cs   // Might add thread count
RabbitMqMetricsPlotBuilder.cs  // Might add queue length
PostgresMetricsPlotBuilder.cs  // Might add connection count

// ❌ Avoid - single generic builder
ResourcePlotBuilder.cs         // Changes affect all plot types
```
