# 01-06. Vertical Slice Ownership

> **Scope:** This is a design principle for human developers and PR reviewers, particularly when evaluating proposals to extract shared builders or base classes. It is not subject to automated audit because deciding when to extract, what constitutes duplication, and where module boundaries fall are inherently context-dependent judgments.

Each builder owns its complete rendering logic. Open the file → see everything it does. No need to navigate elsewhere.

```
// ✅ Good - self-contained vertical slice
ServiceMetricsPlotBuilder.cs  → All service plot logic here
RabbitMqMetricsPlotBuilder.cs → All RabbitMQ plot logic here

// ❌ Avoid - shared "smart" builder that requires navigation
ServiceMetricsPlotBuilder.cs  → Delegates to ResourcePlotBuilder
ResourcePlotBuilder.cs        → Actual logic hidden here
```
