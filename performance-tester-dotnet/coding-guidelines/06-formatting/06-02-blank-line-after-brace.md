# 06-02. Blank Line After Closing Brace

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
