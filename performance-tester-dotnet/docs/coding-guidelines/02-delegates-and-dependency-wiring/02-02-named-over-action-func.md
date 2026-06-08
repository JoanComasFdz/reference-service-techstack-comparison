# 02-02. Prefer Named Delegates Over `Action<T>` / `Func<T>`

A named delegate communicates intent at the type level. This extends **[Guideline 01-04](../01-core-architecture/01-04-descriptive-names.md)** — Descriptive Names to function-typed parameters.

```csharp
// ✅ Good - name says what it does
public delegate void ReportApiLoadProgress(ApiLoadProgress apiLoadProgress);
public delegate Task<Result<Unit, ClearDatabaseError>> ClearDatabase();
public delegate Task<Result<Unit, string>> ClearAllQueues();
public delegate Task<PublishMetrics> PublishEvents(int eventCount);

// ❌ Avoid - only says the shape, not the intent
Action<ApiLoadProgress>              // Could be anything that takes progress
Func<Task<Result<Unit, string>>>     // Could be any async operation
```

**Where to define the delegate:**

- **Next to its data type** if shared across callers (e.g., `ReportApiLoadProgress` next to `ApiLoadProgress` in `ApiLoadProgress.cs`)
- **Inside the consumer** if only used by one caller (e.g., `StartApiLoadTest` inside `ApiTestPhase`)
