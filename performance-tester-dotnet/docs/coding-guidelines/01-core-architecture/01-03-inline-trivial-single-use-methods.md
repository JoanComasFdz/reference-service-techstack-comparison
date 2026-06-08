# 01-03a. Inline Trivial Single-Use Private Methods

A private method that is called exactly once and whose body is a single statement adds indirection without value. The caller already provides the context, and the body is short enough to read in place. Inline it.

**The mechanical test:** a `private` method is a violation when **all three** hold:

1. **Single-statement body** — the method contains exactly one semicolon-terminated statement, or is a single `=>` expression (one comparison, one assignment, one method call, one property access).
2. **Called once** — the method is invoked from exactly one call site within the file (test projects don't count as a second call site).
3. **Not part of a uniform factory-delegate group** (see exception below).

If all three hold, inline the body at the call site and delete the method.

```csharp
// ❌ Avoid — single property assignment, called once
private static void SetTickRotation(Plot plot)
{
    plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
}

// ✅ Inline at the call site
plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
```

```csharp
// ❌ Avoid — single comparison, called once
private static bool IsValid(TimeSpan interval) => interval > TimeSpan.Zero;

if (IsValid(samplingInterval)) { ... }

// ✅ Inline at the call site
if (samplingInterval > TimeSpan.Zero) { ... }
```

```csharp
// ❌ Avoid — single pass-through call, called once
private static void SaveUser(IRepository repo, User user)
{
    repo.Save(user);
}

// ✅ Inline at the call site
repo.Save(user);
```

```csharp
// ✅ Not a violation — body has multiple statements (3), even though called once
private static string? ReadCommandLine(int pid)
{
    var cmdLinePath = $"/proc/{pid}/cmdline";
    if (!File.Exists(cmdLinePath))
        return null;
    return File.ReadAllText(cmdLinePath).Replace('\0', ' ').Trim();
}
```

```csharp
// ✅ Not a violation — called from two separate call sites
private static bool IsExpired(DateTime expiresAt) => expiresAt < DateTime.UtcNow;
```

---

## Exception — Uniform Factory-Delegate Groups

When a group of private single-use methods all follow the same two-step body shape and all feed into one parent call site, extraction is justified even though each method has a small body and is called once. The parent call site becomes a clean table of contents that would collapse into anonymous blocks if inlined.

**Suppress the finding when all three properties hold:**

1. **Uniform body shape** — every method in the group (≥ 3 siblings) has the same structure: build a `deps` object, then return a delegate that closes over it.
2. **Name echoes property** — the method name mirrors the record property or argument it configures at the call site (e.g., `BuildRunSetup` → `RunSetup:`).
3. **Bake-in content** — the returned delegate closes over captured variables (`deps`, `logger`, `ct`, `config`), meaning the body cannot be reduced to a single `=>` expression.

```csharp
// ✅ Good — uniform factory-delegate group (N = 5, all same shape)
return new Dependencies(
    RunSetup:     BuildRunSetup(services, clearDatabase, clearAllQueues, config, logger, ct),
    RunWarmup:    BuildRunWarmup(trackEvents, publishEvents, clearDatabase, clearAllQueues, config, logger, ct),
    RunEventTest: BuildRunEventTest(services, trackEvents, publishEvents, config, reportProgress, logger, ct),
    RunApiTest:   BuildRunApiTest(services, config, reportProgress, logger, ct),
    RunReporting: BuildRunReporting(services, config, logger, ct));

// Each body follows the identical two-step shape:
private static RunSetupDelegate BuildRunSetup(...)
{
    var deps = SetupPhaseModule.BuildDependencies(services, clearDatabase, clearAllQueues, config, ct);
    return (testRunId) => SetupPhaseModule.ExecuteAsync(testRunId, deps, logger);
}
```

```csharp
// ❌ Avoid — inlining collapses the table into anonymous blocks:
return new Dependencies(
    RunSetup: (testRunId) =>
    {
        var deps = SetupPhaseModule.BuildDependencies(services, clearDatabase, clearAllQueues, config, ct);
        return SetupPhaseModule.ExecuteAsync(testRunId, deps, logger);
    },
    RunWarmup: () =>
    {
        var deps = WarmupPhaseModule.BuildDependencies(trackEvents, publishEvents, clearDatabase, clearAllQueues);
        return WarmupPhaseModule.ExecuteAsync(config, deps, logger, ct);
    },
    // ... 3 more blocks — table of contents is gone
    );
```

**Not the pattern** — a single isolated private method that does not belong to a uniform group of ≥ 3 identically-shaped siblings. A one-off extraction should be inlined per the main rule.