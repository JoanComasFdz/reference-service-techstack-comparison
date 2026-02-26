# 04-01. Use Value Objects for Constrained Primitives

```csharp
// ✅ Good - invalid state is unrepresentable
public sealed record EventCount
{
    public int Value { get; }
    private EventCount(int value) => Value = value;

    public static Result<EventCount, string> Create(int value) =>
        value is >= 1 and <= 1_000_000
            ? new Success(new EventCount(value))
            : new Failure($"Events must be between 1 and 1,000,000 (got: {value})");

    public override string ToString() => Value.ToString();
}

// ❌ Avoid - raw int with validation scattered across callers
public record TestConfiguration(int EventCount = 10000, ...);
// Then in ValidateOptions:
if (options.Events < 1 || options.Events > 1_000_000) return "error";
// Then in another caller: same check duplicated or forgotten
```

**When to use value objects:**

- The primitive has a valid range or format (e.g., 1–1,000,000)
- Multiple callers need to trust the value is valid
- The constraint is a domain rule, not a one-off check
- A constructor or method validates a primitive parameter with a throw guard — the guard is a signal that the parameter needs a Value Object. Move the validation into `Create()`; the constructor receives an already-valid type and needs no guard at all (see also [Guideline 03-01](../03-error-handling/03-01-result-over-exceptions.md)).

```csharp
// ❌ Signal — constructor throw guard on a primitive: the parameter needs a Value Object
internal sealed class ProcessMonitorBackgroundService : BackgroundService
{
    public ProcessMonitorBackgroundService(TimeSpan samplingInterval, ILogger logger)
    {
        if (samplingInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(samplingInterval));  // ← signal
    }
}

// ✅ Fix — Value Object carries the Result; constructor receives an already-valid type
public sealed record SamplingInterval
{
    public TimeSpan Value { get; }
    private SamplingInterval(TimeSpan value) => Value = value;

    public static Result<SamplingInterval, string> Create(TimeSpan value) =>
        value > TimeSpan.Zero
            ? new Success(new SamplingInterval(value))
            : new Failure($"Sampling interval must be positive (got: {value})");

    public override string ToString() => Value.ToString();
}

internal sealed class ProcessMonitorBackgroundService : BackgroundService
{
    public ProcessMonitorBackgroundService(SamplingInterval samplingInterval, ILogger logger)
    {
        // No guard needed — SamplingInterval.Create() already ensured validity
    }
}
```

**When NOT to use value objects:**

- The primitive is unconstrained (any `int` is fine)
- The constraint is only checked once at a single boundary
- The overhead outweighs the clarity (e.g., internal loop counters)
