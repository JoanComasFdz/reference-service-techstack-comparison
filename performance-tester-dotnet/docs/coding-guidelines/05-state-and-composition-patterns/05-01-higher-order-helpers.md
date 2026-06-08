# 05-01. Higher-Order Helper Functions for Structural Duplication

When the same structure (e.g., iterate + await all) is repeated across call sites with only the operation changing, extract the structure as a function that takes a function.

```csharp
// ❌ Structural duplication — same pattern, different operation
async () => { await Task.WhenAll(monitors.Select(m => m.WarmupAsync(ct))); }
async () => { await Task.WhenAll(monitors.Select(m => m.StartAsync(ct))); }

// ✅ Higher-order helper captures the repeated structure
Task forAllMonitors(Func<IMonitor, Task> action) => Task.WhenAll(monitors.Select(action));
() => forAllMonitors(m => m.WarmupAsync(ct))
() => forAllMonitors(m => m.StartAsync(ct))
```

**When to use:** Two or more call sites share identical structure but plug in different operations.

**When NOT to use:** Only one call site, or the structure is trivial (one-liner with no repetition).
