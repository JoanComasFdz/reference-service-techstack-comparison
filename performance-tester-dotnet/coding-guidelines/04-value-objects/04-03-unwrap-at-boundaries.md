# 04-03. Unwrap `.Value` at Boundaries, Not Everywhere

Downstream interfaces (e.g., `IEventPublisher.PublishEventsAsync(int count)`) still accept primitives. Unwrap `.Value` at the call site where the boundary is crossed.

```csharp
// ✅ Good - unwrap at the boundary
await _eventPublisher.PublishEventsAsync(config.EventCount.Value, cancellationToken);
NumEvents = config.EventCount.Value;  // assigning to int property
var rate = config.EventCount.Value / duration.TotalSeconds;  // arithmetic

// ✅ Good - no unwrap needed for string interpolation (ToString() handles it)
_logger.LogInformation("Processing {Count} events", config.EventCount);

// ❌ Avoid - unwrapping everywhere "just in case"
var count = config.EventCount.Value;
_logger.LogInformation("Processing {Count} events", count);
```
