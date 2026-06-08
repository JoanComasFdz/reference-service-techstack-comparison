# 06-04. Expression Body (`=>`) Stays on the Same Line

When using expression-bodied members or lambda expressions, the expression after `=>` must start on the **same line** as the arrow. Never put a bare `=>` at the end of a line with the expression starting on the next line.

```csharp
// ✅ Good - expression on same line as =>
public override string ToString() => Value.ToString();

// ✅ Good - short lambda on same line
var names = items.Select(x => x.Name);

// ✅ Good - multi-param declaration with each param on its own line, expression on => line
public static Result<EventCount, string> Create(
    int value,
    int maxValue) => value is >= 1 and <= maxValue
        ? new Success(new EventCount(value))
        : new Failure($"Invalid (got: {value})");

// ✅ Good - when the expression is complex, open a block body instead
public static Result<EventCount, string> Create(int value)
{
    if (value is < 1 or > 1_000_000)
    {
        return new Failure($"Events must be between 1 and 1,000,000 (got: {value})");
    }

    return new Success(new EventCount(value));
}

// ❌ Avoid - newline right after =>
public override string ToString()
    => Value.ToString();

// ❌ Avoid - bare => at end of line (regular method returning a value)
public int GetTotal(int a, int b) =>
    a + b;
```

**Why:** The expression after `=>` is the most important part — it's _what the function does_. Pushing it to the next line hides it. If the expression is too long for one line, switch to a block body `{ }` instead of dangling the arrow.

**Exception — expression-bodied definitions and lambdas that delegate to another call:** When an expression-bodied member or lambda exists solely to forward arguments to another method (a delegation pattern), it is acceptable for `=>` to end the line with the delegated call on the next line. Converting these to block bodies (`{ return ...; }`) adds syntactic noise without improving readability.

```csharp
// ✅ Good - expression-bodied delegation: => at end of line is acceptable
public static Result<ApiDuration, string> Create(string duration) =>
    Create(duration, "API duration", v => new ApiDuration(v));

// ✅ Good - lambda delegation in factory wiring
SharedPhaseDelegates.TrackEventsDelegate trackEvents = (count, timeout, reportProgress) =>
    eventConsumer.StartTrackingEventsAsync(
        count,
        timeout,
        reportProgress,
        ct);

// ❌ Avoid - wrapping a simple delegation in braces adds noise
public static Result<ApiDuration, string> Create(string duration)
{
    return Create(duration, "API duration", v => new ApiDuration(v));
}
```

**Not enforced by `.editorconfig`** (no built-in rule). Enforced by convention and code review.
