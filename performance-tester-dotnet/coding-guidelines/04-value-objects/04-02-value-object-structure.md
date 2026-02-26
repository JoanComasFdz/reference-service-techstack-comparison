# 04-02. Value Object Structure

Follow this exact structure for consistency:

```csharp
using PerformanceTester.Functional;
using static PerformanceTester.Functional.Result<Namespace.ValueType, string>;

namespace PerformanceTester.Orchestration.ValueObjects;

// 1. Sealed record with private constructor
public sealed record EventCount
{
    public int Value { get; }
    private EventCount(int value) => Value = value;

    // 2. Static factory returning Result<T, string>
    public static Result<EventCount, string> Create(int value) =>
        value is >= 1 and <= 1_000_000
            ? new Success(new EventCount(value))
            : new Failure($"Events must be between 1 and 1,000,000 (got: {value})");

    // 3. ToString override for string interpolation
    public override string ToString() => Value.ToString();
}
```

**Key elements:**

- **`sealed record`** — immutable, value equality, cannot be subclassed
- **Private constructor** — forces callers through `Create()`
- **`Result<T, string>` for errors** — the error message lives next to the constraint, consumers just display it. Use a Dunet union error type only when callers need to branch on different failure kinds (e.g., `ClearDatabaseError` with `EmptyName`, `DatabaseNotFound`, `RetriesExhausted`)
- **`Create()` accepts the wider type** — e.g., `Create(int)` even if internal storage is `ushort`, to avoid casting noise at call sites
- **`ToString()` override** — enables seamless use in string interpolation and structured logging
