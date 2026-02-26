# 05-02. Behavioral Decisions Belong in the Consumer, Not the Caller

When a class receives a shared function whose signature has a parameter it doesn't need, that class should accept the full signature and provide the default internally. The caller shouldn't encode knowledge about what the consumer does or doesn't care about.

```csharp
// ❌ Caller decides what the consumer needs — leaks knowledge outward
trackEvents: (count, timeout) => trackEvents(count, timeout, null),

// ✅ Consumer accepts the full signature and decides for itself
var task = trackEvents(count, timeout, progress: null);
```

**Why:** The consumer owns its behavior. If it later starts using the parameter, only the consumer changes — no caller needs updating.
