# 01-03. Inline Single-Use Code

The real problem is a **vague name wrapping a trivial body**. A method called `IsValid` that contains `return value > TimeSpan.Zero` creates indirection without meaning — the expression already tells the full story, and the name tells you nothing the expression doesn't. In that case, inline it.

**When to inline:** the body is a trivial expression and the name adds no meaning beyond what the expression already communicates.

**When to keep a single-use method:** the name is specific and the body encapsulates a coherent multi-step operation whose steps would clutter the caller if inlined.

```csharp
// ❌ Avoid — vague name over a trivial body
if (IsValid(samplingInterval)) { ... }
private static bool IsValid(TimeSpan interval) => interval > TimeSpan.Zero;

// ✅ Option A — inline: the expression is already readable
if (samplingInterval > TimeSpan.Zero) { ... }

// ✅ Option B — rename: a specific name can justify extraction even for a one-liner
if (IsPositiveDuration(samplingInterval)) { ... }
private static bool IsPositiveDuration(TimeSpan interval) => interval > TimeSpan.Zero;
```

A method whose name precisely labels a multi-step operation is worth keeping even when called once:

```csharp
// ✅ Good — specific name, coherent multi-step body; inlining would clutter the caller
private static string? ReadCommandLine(int pid)
{
    var cmdLinePath = $"/proc/{pid}/cmdline";
    if (!File.Exists(cmdLinePath))
        return null;
    return File.ReadAllText(cmdLinePath).Replace('\0', ' ').Trim();
}

// ❌ Avoid — one-liner with a vague name; write it at the call site instead
private static void SetTickRotation(Plot plot)
{
    plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
}
```

The question to ask: **Does the name describe something the expression can't say at a glance?** If yes, keep the method. If no, inline or rename to something specific.

**Exception — Uniform factory-delegate groups:** When a group of private single-use methods all follow the same two-step body shape and all feed into one parent call site, extraction is justified even though no method is reused. Identify the pattern by checking all three properties:

1. **Uniform body shape** — every method in the group has the same structure: build a `deps` object, then return a delegate that closes over it.
2. **Name echoes property** — the method name mirrors the record property it configures (e.g., `BuildRunSetup` → `RunSetup:`), so the name acts as a label, not an abstraction.
3. **Bake-in content** — the returned delegate closes over captured variables (`deps`, `logger`, `ct`, `config`), meaning the body cannot be reduced to a single `=>` expression suitable for inlining.

When all three hold, the parent call site becomes a clean table of contents. Inlining would replace each labeled argument with a multi-line anonymous block, collapsing the table structure and making the wiring harder to scan.

```csharp
// ✅ Good — uniform factory-delegate group (N = 5, all same shape)
// Parent site reads as a table of contents:
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

// ❌ Avoid — inlining collapses the table into anonymous blocks that obscure structure:
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

**Not the pattern** — a single isolated private method that does not belong to a uniform group of identically-shaped siblings. A one-off extraction should be inlined per the main rule.
