# Formatting Rules

> Guidelines 06-01 through 06-04. For the full index and routing table, see [CODING_GUIDELINES.md](../CODING_GUIDELINES.md).

These rules are enforced by `.editorconfig` where possible and by convention otherwise. Run `dotnet format` to auto-fix violations.

---

### 06-01. Always Use Braces in Control Flow Statements

Every `if`, `else`, `for`, `foreach`, `while`, `do`, and `using` must use braces, even when the body is a single line. This prevents bugs when lines are added later and makes the code structure unambiguous.

```csharp
// ✅ Good - braces always present
if (result.IsFailure)
{
    return Fail(TestPhase.Setup, result.FailureError);
}

foreach (var item in items)
{
    Process(item);
}

// ❌ Avoid - braceless single-line body
if (result.IsFailure)
    return Fail(TestPhase.Setup, result.FailureError);

foreach (var item in items)
    Process(item);
```

**Enforced by:** `.editorconfig` rule `csharp_prefer_braces = true:warning`

**No exceptions.** Even guard clauses and early returns use braces. The visual consistency outweighs the marginal brevity.

### 06-02. Blank Line After Closing Brace

Every closing brace `}` must be followed by a blank line, **except** when the next line is:

- Another closing brace `}`
- An `else`, `catch`, or `finally` keyword (continuation of the same statement)

This gives each block visual breathing room and makes the code scannable.

```csharp
// ✅ Good - blank line after each block
if (setupResult.IsFailure)
{
    return Fail(TestPhase.Setup, setupResult.FailureError);
}

var serviceProcessId = setupResult.SuccessValue;

try
{
    var warmupResult = await WarmupPhase.ExecuteAsync(...);

    if (warmupResult.IsFailure)
    {
        return Fail(TestPhase.Warmup, warmupResult.FailureError);
    }

    var warmupEndTime = DateTime.UtcNow;
}
catch (Exception ex)
{
    logger.LogError(ex, "Unexpected error");
}
finally
{
    await Cleanup();
}

// ❌ Avoid - no blank line after closing brace
if (setupResult.IsFailure)
{
    return Fail(TestPhase.Setup, setupResult.FailureError);
}
var serviceProcessId = setupResult.SuccessValue;
```

**Not enforced by `.editorconfig`** (no built-in rule). Enforced by convention and code review. Consider adding `StyleCop.Analyzers` (rule `SA1513`) if build-time enforcement is desired.

### 06-03. All-or-Nothing Parameter Wrapping

Parameters in method calls and declarations must be **all on one line** or **each on its own line**. Never group multiple parameters on a continuation line (partial wrap).

```csharp
// ✅ Good - all parameters on one line
var result = await TestReportLoader.LoadFromFolderAsync(folder, cancellationToken);

// ✅ Good - each parameter on its own line
var result = await SetupPhase.ExecuteAsync(
    testRunId,
    clearDatabase,
    findServiceProcessId,
    logger);

// ❌ Avoid - partial wrap (multiple params grouped on continuation line)
var result = await TestReportLoader.LoadFromFolderAsync(
    folder, cancellationToken);

// ❌ Avoid - partial wrap in logging
_logger.LogWarning("⚠️ Attempt {Attempt}/{Max} failed, retrying in {Delay}s...",
    attempt, MaxRetries, _retryDelay.TotalSeconds);

// ✅ Good - logging: all on one line if it fits
_logger.LogWarning("⚠️ Attempt {Attempt}/{Max} failed", attempt, MaxRetries);

// ✅ Good - logging: each arg on its own line if it doesn't fit
_logger.LogWarning(
    "⚠️ Attempt {Attempt}/{Max} failed, retrying in {Delay}s...",
    attempt,
    MaxRetries,
    _retryDelay.TotalSeconds);
```

**The rule:** If any parameter needs to wrap, **all** parameters wrap — one per line. This makes diffs cleaner (adding a parameter changes one line, not a reformatted group) and makes the call site scannable.

**Applies to:** Method calls, method declarations, constructor calls, delegate invocations, `new()` expressions, attribute parameters.

**Not enforced by `.editorconfig`** (no built-in rule). Enforced by convention and code review.

### 06-04. Expression Body (`=>`) Stays on the Same Line

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
