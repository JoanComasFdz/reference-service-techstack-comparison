# 05-04. Context Record Pattern (Shared Mutable State)

When a class or function group has mutable state, centralize all mutable state in a single record. Pass the record explicitly to static functions. The value is **visibility** — all state that can change lives in one place.

Apply at the first sign of mutable state — consistency matters more than saving a record definition.

```csharp
// ✅ Good — all mutable state visible in one record
internal sealed record MonitorContext(NonEmptyString ContainerName)
{
    public ConcurrentBag<DockerMetrics> CollectedMetrics { get; } = new();
    public TaskCompletionSource StartSignal { get; } = new();
    public bool StreamingFailed { get; set; }
}

internal static class MonitoringOperations
{
    public static async Task RunStreamingLoopAsync(
        MonitorContext ctx,
        ILogger logger,
        CancellationToken ct)
    {
        ctx.CollectedMetrics.Add(metric);  // Thread-safe mutation
    }
}

// ❌ Avoid — mutable state scattered across private fields
internal sealed class DockerMonitorService
{
    private readonly ConcurrentBag<DockerMetrics> _collectedMetrics = new();
    private readonly TaskCompletionSource _startSignal = new();
    private volatile bool _streamingFailed;
    // Must read entire class to find all mutable state
}
```

**Why mutable, not immutable?** When state is shared across concurrent tasks, returning a new record doesn't work — other threads still hold the old reference:

```csharp
// ❌ Broken — Thread B still holds the old reference
// Thread A:
ctx = ctx with { StreamingFailed = true };  // creates new record

// Thread B (still holds original ctx):
if (ctx.StreamingFailed) { ... }  // always false — different reference
```

Types like `ConcurrentBag<T>`, `TaskCompletionSource`, and `SemaphoreSlim` are inherently mutable — other code holds references to the original instances. Copying them into a new record would fork the state. For single-threaded pipelines where immutability IS possible, see Guideline 05-05.

**When to use:** Any class or function group that has mutable state — even a single field.

**When NOT to use:** Purely stateless functions (like `DatabaseCleaner`, `RabbitMqCleaner`) — no state to extract.

**Structure:**

- `sealed record` with constructor parameters for fixed identity (e.g., `ContainerName`)
- Mutable properties: `{ get; set; }` or self-initializing collections (`= new()`)
- No logic in the record — it's a state bag, not a service

**Relationship to other guidelines:**

- Extends **[Guideline 01-02](../01-core-architecture/01-02-explicit-parameters.md)** (explicit parameters) from single values to state bundles
- Used by **[Guideline 05-06](05-06-thin-shell-pattern.md)** (thin shell) as the state extraction technique
- For sequential code, prefer **[Guideline 05-05](05-05-immutable-state-threading.md)** (immutable state threading)
