# 05-05. Immutable State Threading (Sequential Pipelines)

When state flows through a single-threaded pipeline — no concurrent access, one step at a time — return a **new** record from each step instead of mutating. The caller reassigns the variable; the record itself is never mutated. This is the FP fold/reduce pattern in C#.

```csharp
// ✅ Good — each step returns new state, no mutation
internal sealed record ParseState(
    int LinesProcessed,
    int ErrorCount,
    bool HeaderFound);

internal static class LogParser
{
    public static ParseState ProcessLine(ParseState state, string line) =>
        IsError(line)
            ? state with { LinesProcessed = state.LinesProcessed + 1, ErrorCount = state.ErrorCount + 1 }
            : state with { LinesProcessed = state.LinesProcessed + 1 };
}

// Usage — pure fold via LINQ Aggregate
var state = lines.Aggregate(
    new ParseState(0, 0, false),
    LogParser.ProcessLine);

// ❌ Avoid — mutable fields in a single-threaded pipeline
var linesProcessed = 0;
var errorCount = 0;
foreach (var line in lines)
{
    linesProcessed++;
    if (IsError(line)) errorCount++;
}
```

**Why this works here but not in Guideline 05-04:** There is only one reference to the state. Reassigning `state = ...` updates the only copy. No other thread holds a stale reference.

**When to use:** Sequential processing (loops, pipelines, fold/reduce patterns) where state accumulates across steps but is only accessed by one thread.

**When NOT to use:**

- State is shared across concurrent tasks — use Guideline 05-04 (mutable context record)
- State contains inherently mutable types (`ConcurrentBag`, `TaskCompletionSource`) — these can't be copied meaningfully

**Structure:**

- `sealed record` with all properties in the constructor (positional record)
- All properties are immutable (no `{ get; set; }`)
- Functions return the record type (the new state), not `void`
- Caller uses `Aggregate` or `state = Function(state, input)` pattern

**Relationship to other guidelines:**

- Companion to **[Guideline 05-04](05-04-context-record-pattern.md)** — same idea (explicit state), different concurrency model
- Extends **[Guideline 01-01](../01-core-architecture/01-01-static-classes.md)** (static classes) — static functions that transform state
- Extends **[Guideline 01-02](../01-core-architecture/01-02-explicit-parameters.md)** (explicit parameters) — state is an input AND an output
