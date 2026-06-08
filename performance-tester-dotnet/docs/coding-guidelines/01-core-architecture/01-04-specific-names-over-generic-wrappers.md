# 01-03b. Prefer Specific Names Over Generic Wrappers

> **Scope:** This is a code-review principle for human developers and PR reviewers. It is not subject to automated audit because name quality is inherently context-dependent.

When you extract a helper method, its name should tell the reader something the body cannot say at a glance. A name that merely restates what the expression already communicates adds a layer of indirection without adding meaning.

**The question to ask:** If I inline this method, does the call site lose information? If the expression reads just as clearly without the method name, the name isn't earning its keep — either inline it (see [01-03a](./rule-01-03a.md)) or rename it to something that adds domain meaning.

## Vague Names That Restate the Expression

These names act as synonyms for the body. The reader still has to open the method to understand what "valid" or "ready" means in this context.

```csharp
// ⚠️ Vague — "IsValid" for a TimeSpan just means "> Zero", which the expression already says
private static bool IsValid(TimeSpan interval) => interval > TimeSpan.Zero;

// ⚠️ Vague — "CheckStatus" just wraps a null check
private static bool CheckStatus(Order? order) => order?.Status != null;

// ⚠️ Vague — "Process" says nothing about what processing means
private static void Process(Message msg) => _handler.Handle(msg);
```

Common vague prefixes to watch for during review: `IsValid`, `Check`, `Validate`, `Process`, `Handle`, `Execute`, `Do`, `Run`, `Apply`, `Set{Property}`.

These are not _always_ vague — `IsValid` on a `LicenseKey` with a checksum algorithm inside may be perfectly clear. The point is that these names deserve extra scrutiny: does the name add meaning, or just add a click?

## Specific Names That Earn Their Extraction

A name earns its extraction when it introduces a domain concept that the raw expression doesn't convey, or when it labels a coherent operation whose steps would clutter the caller.

```csharp
// ✅ "IsPositiveDuration" tells the reader the domain intent, not just the mechanic
private static bool IsPositiveDuration(TimeSpan interval) => interval > TimeSpan.Zero;

// ✅ "ReadCommandLine" names a coherent multi-step OS operation
private static string? ReadCommandLine(int pid)
{
    var cmdLinePath = $"/proc/{pid}/cmdline";
    if (!File.Exists(cmdLinePath))
        return null;
    return File.ReadAllText(cmdLinePath).Replace('\0', ' ').Trim();
}

// ✅ "ComputeExponentialBackoff" labels a formula the expression alone wouldn't explain
private static TimeSpan ComputeExponentialBackoff(int attempt)
    => TimeSpan.FromSeconds(Math.Pow(2, attempt)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
```

## Heuristics for Code Review

During PR review, consider these signals:

- **Could you explain the method by just reading its name aloud?** If the name forces you to say "it checks if... um... let me look at the body," it's probably too vague.
- **Does the name use a domain term?** `IsPositiveDuration`, `HasExceededRateLimit`, `ReadCommandLine` anchor the reader in the problem domain. `IsValid`, `Check`, `Process` don't.
- **Would inlining lose a useful label?** If the call site becomes harder to scan after inlining — because the body is multi-step or the expression is dense — the name is earning its keep.
- **Is the method a single expression?** Single-expression methods face a higher naming bar. The name must add real meaning to justify the indirection, because the body is already trivially readable. Multi-statement methods get more slack because the name serves as a summary.
