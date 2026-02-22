# Value Objects (Eliminating Primitive Obsession)

> Guidelines 18-22. For the full index and routing table, see [CODING_GUIDELINES.md](../CODING_GUIDELINES.md).

When a primitive (`int`, `string`, `TimeSpan`) has domain rules (valid range, format, non-empty), wrap it in a sealed record with a `Create()` factory returning `Result<T, TError>`. Once constructed, the value is guaranteed valid — "parse, don't validate."

---

### 18. Use Value Objects for Constrained Primitives

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

**When NOT to use value objects:**

- The primitive is unconstrained (any `int` is fine)
- The constraint is only checked once at a single boundary
- The overhead outweighs the clarity (e.g., internal loop counters)

### 19. Value Object Structure

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

### 20. Unwrap `.Value` at Boundaries, Not Everywhere

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

### 21. No Unit Tests for Value Objects

Value object validation logic (range checks, format checks) is trivially correct by inspection. The factory + Result pattern makes invalid construction impossible at compile time. Existing integration and validator tests exercise the parse path indirectly.

```csharp
// ✅ The factory IS the test — invalid values can't exist
EventCount.Create(0)       // → Failure("Events must be between 1 and 1,000,000 (got: 0)")
EventCount.Create(10000)   // → Success(EventCount(10000))
EventCount.Create(1000001) // → Failure("Events must be between 1 and 1,000,000 (got: 1000001)")

// ❌ Avoid - unit tests that restate the range check
[Fact] void Create_WithZero_ReturnsFailure() { ... }  // Just restating the condition
```

**Exception:** If a value object has complex parsing logic (regex, multi-step validation), tests may be warranted.

### 22. Value Object Families via Base Record

When multiple value objects share identical validation but represent distinct domain concepts, use a non-sealed base `record` with a `protected` constructor and a generic `Create<T>` factory. Derive sealed tag types that delegate to the base. This eliminates code duplication while providing compile-time swap prevention — you cannot accidentally pass a `RabbitMqContainerName` where a `PostgresContainerName` is expected.

```csharp
// ✅ Good - base record owns shared validation, derived types are tag types
public record ContainerName
{
    public string Value { get; }
    protected ContainerName(string value) => Value = value;

    public override string ToString() => Value;

    protected static Result<T, string> Create<T>(string value, string displayName, Func<string, T> factory)
        where T : ContainerName =>
        !string.IsNullOrWhiteSpace(value)
            ? new Result<T, string>.Success(factory(value.Trim()))
            : new Result<T, string>.Failure($"{displayName} container name cannot be empty");
}

public sealed record RabbitMqContainerName : ContainerName
{
    private RabbitMqContainerName(string value) : base(value) { }

    public static Result<RabbitMqContainerName, string> Create(string value) =>
        Create(value, "RabbitMQ", v => new RabbitMqContainerName(v));

    public static RabbitMqContainerName FromString(string value) => new(value);
}

public sealed record PostgresContainerName : ContainerName
{
    private PostgresContainerName(string value) : base(value) { }

    public static Result<PostgresContainerName, string> Create(string value) =>
        Create(value, "PostgreSQL", v => new PostgresContainerName(v));

    public static PostgresContainerName FromString(string value) => new(value);
}

// ❌ Avoid - single type with label parameter to distinguish at runtime
public sealed record ContainerName
{
    public static Result<ContainerName, string> Create(string value, string label) =>
        ...new Failure($"{label} container name cannot be empty");
}
// Call sites can swap arguments without compiler error:
var config = new TestConfiguration(
    RabbitMqContainerName: ContainerName.Create(postgresName, "RabbitMQ"),  // Bug! Wrong value, compiles fine
    PostgresContainerName: ContainerName.Create(rabbitMqName, "PostgreSQL"));
```

**When to use this pattern:**

- Two or more value objects have identical validation logic
- They appear as separate parameters in the same method/record (swap risk)
- The base validation can be parameterized (e.g., `displayName` for error messages)

**Key elements:**

- **Base `record`** (not `sealed`) — owns `Value`, `ToString()`, and `protected static Create<T>`
- **Derived `sealed record`** — tag type, private constructor, one-liner `Create()` delegates to base
- **`Create<T>` uses explicit `Result<T, string>` constructors** — cannot use `using static` because `T` is generic
- **`FromString()` on each derived type** — for test builders and known-valid paths
