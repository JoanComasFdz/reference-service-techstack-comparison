# 03-01. Use Result Types Instead of Exceptions for Expected Failures

Reserve exceptions for bugs and truly unexpected situations (out of memory, network down). For failures that are **part of the normal domain** (invalid input, resource not found, validation errors), return a `Result<TSuccess, TFailure>`.

```csharp
// ✅ Good - expected failure expressed in the return type
public static Result<TimeSpan, DurationParseError> Parse(string duration)
{
    if (string.IsNullOrWhiteSpace(duration))
        return new Failure(new DurationParseError.Empty());

    // ...
    return new Success(TimeSpan.FromSeconds(value));
}

// ❌ Avoid - exception for expected input validation
public static TimeSpan Parse(string duration)
{
    if (string.IsNullOrWhiteSpace(duration))
        throw new ArgumentException("Duration cannot be empty");

    // ...
    return TimeSpan.FromSeconds(value);
}
```

**Choosing `TFailure`:** Use a typed discriminated union (e.g., `DurationParseError`) when the caller needs to distinguish between different failure reasons. Use `string` when a human-readable message is sufficient.

**Result variant selection:**

|               | Simple failure (`string`) | Typed failure (`TFailure`) |
| ------------- | ------------------------- | -------------------------- |
| **Has value** | `Result<TValue>`          | `Result<TValue, TFailure>` |
| **Void**      | `Result<Unit>`            | `Result<Unit, TFailure>`   |

**Placement constraint:** `[Union]` types must be declared at namespace level, never nested inside another class. Dunet's source generator emits `public` extension methods at namespace level that reference the union type — nesting it inside an `internal` class causes CS0051 (inconsistent accessibility). When a `[Union]` type logically belongs to a module, place it at namespace level in the same file and link it with a `<see cref="...Module"/>` doc comment. See also Guideline 05-06.

**Naming failure union types:** Use `{MethodAction}Error` — the name describes what failed, not where. Each variant carries contextual data. Define the union alongside the method that returns it.

```csharp
// ✅ Good - name describes the failed action, variants carry context
[Union]
public partial record ParseLineError
{
    public partial record EmptyInput;
    public partial record InvalidJson(string RawLine);
    public partial record IrrelevantMetric(string MetricName);
}

[Union]
public partial record ClearDatabaseError
{
    public partial record EmptyName;
    public partial record DatabaseNotFound(string Name);
    public partial record RetriesExhausted(int Attempts, Exception Last);
}

// ❌ Avoid - generic name, no context in variants
[Union]
public partial record AppError
{
    public partial record ValidationFailed;
    public partial record NotFound;
}
```

> **03-01 vs [Guideline 04-01](../04-value-objects/04-01-use-value-objects.md) — constructor guards for primitive parameters:** When a constructor validates a primitive parameter and throws (e.g., `if (samplingInterval <= TimeSpan.Zero) throw ...`), the preferred fix is **not** a Result-returning factory on the enclosing class. It is a **Value Object** for that parameter ([Guideline 04-01](../04-value-objects/04-01-use-value-objects.md)). The Value Object's `Create()` returns `Result`; the constructor then receives an already-valid type and needs no guard. **Do NOT report such a throw guard as a 03-01 violation — it belongs exclusively to [Guideline 04-01](../04-value-objects/04-01-use-value-objects.md).**
>
> Apply a Result-returning factory on the enclosing class only when the class itself has construction failures that aren't reducible to a single constrained parameter (e.g., establishing a connection, parsing a composite configuration from multiple inputs).
>
> **03-01 vs [Guideline 03-05](03-05-no-null-checks-in-constructors.md) — null guards on DI-injected parameters:** A `?? throw new ArgumentNullException(...)` guard on a DI-injected constructor parameter is **not** a 03-01 violation — it belongs exclusively to [Guideline 03-05](03-05-no-null-checks-in-constructors.md). Do NOT report it here.
