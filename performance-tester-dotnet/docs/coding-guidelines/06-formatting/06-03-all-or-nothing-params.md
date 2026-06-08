# 06-03. All-or-Nothing Parameter Wrapping

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
