# 02-01. Use Named Delegates for Single-Operation Dependencies

When a dependency is a single operation (one method), use a named `delegate` instead of an interface. Lighter than an interface, more descriptive than raw `Action<T>`/`Func<T>`.

```csharp
// ✅ Good - named delegate for a single operation
public delegate void ReportApiLoadProgress(ApiLoadProgress apiLoadProgress);

// ✅ Good - named delegate with richer signature
public delegate Task<ApiLoadTestResult> StartApiLoadTest(
    string targetUrl,
    TimeSpan duration,
    int virtualUsers,
    ReportApiLoadProgress progress,
    int maxConsecutiveFailures,
    string? scriptDirectory);

// ❌ Avoid - single-method interface (ceremony without benefit)
public interface IApiLoadProgressReporter
{
    void Report(ApiLoadProgress progress);
}

// ❌ Avoid - raw Action<T> that loses semantic meaning
public static async Task ExecuteAsync(Action<ApiLoadProgress> progress) { ... }
```

**When to use:**

- The dependency is a single operation, not a family of related operations
- The caller only needs to supply one behavior

**When NOT to use:**

- The dependency has multiple related methods that change together → use an interface
- The dependency needs DI container registration at a slice boundary → use an interface (see Guideline 02-03)
