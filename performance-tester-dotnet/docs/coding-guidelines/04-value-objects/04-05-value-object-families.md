# 04-05. Value Object Families via Base Record

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
