# 01-02. Explicit Parameters Over Hidden State

Pass all dependencies as method parameters, not constructor injection. Makes data flow visible at the call site.

```csharp
// ✅ Good - all inputs explicit
public static Plot Build(ResourceMetricsReport? data, ChartConfig config)

// ❌ Avoid - hidden dependency
public Plot Build(ResourceMetricsReport? data)  // uses _config from field
```
