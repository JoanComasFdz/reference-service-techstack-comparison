# 02-04. Minimize Interface Reach with Dependency Composition

Classes should depend on **pre-composed capabilities**, not on the interfaces or individual operations behind them. Interfaces are a DI registration concern — contain them in a **dependencies class**: a static factory that resolves interfaces and produces bound delegates at the right abstraction level. Consumers receive delegates matching their actual abstraction level.

```csharp
// ✅ Good — dependencies class composes phases, orchestrator sees only phase delegates
internal static class OrchestratorDependencies
{
    public static OrchestratorDeps Build(
        IServiceProvider services, Config config, CancellationToken ct) => new(
        RunSetup: (testRunId) => SetupPhase.ExecuteAsync(testRunId,
            clearDb: () => services.GetRequiredService<IDatabase>().ClearAsync(config.Db, ct),
            ...),
        RunProcess: () => ProcessPhase.ExecuteAsync(
            start: () => services.GetRequiredService<IProcessRunner>().StartAsync(ct),
            ...));
}

// Consumer — knows only about phases, not their internals
internal static class Orchestrator
{
    public static async Task<Result<Report, Error>> RunAsync(OrchestratorDeps deps)
    {
        var setup = await deps.RunSetup(Guid.NewGuid());
        var result = await deps.RunProcess();
        ...
    }
}

// ❌ Avoid — consumer depends on every individual operation
public class Orchestrator(IDatabase db, IProcessRunner runner, IEventPublisher publisher)
{
    // Knows about clearing databases, starting processes, publishing events...
    // Should only know about phases.
}
```

**Why:**

- Classes should know about their dependencies at their abstraction level, not every small operation
- Dependency composition classes centralize plumbing (interface resolution, config binding, CT binding)
- Consumers become testable with simple lambdas at the right granularity
- Adding a new operation inside a phase doesn't change the orchestrator

**The pattern: Configure → Build → Run**

1. **Configure:** Register interfaces in DI (`AddInfrastructure()`, `AddEventPublishing()`, etc.)
2. **Build:** Dependencies class resolves interfaces, composes them into capability-level delegates
3. **Run:** Consumer calls delegates with zero knowledge of interfaces or internal operations

**When to use:**

- Any class that coordinates multiple components (orchestrators, pipelines)
- Any class where you use only specific methods from broader interfaces
- Any static class that needs "injected" capabilities

**When NOT to use:**

- Inside the dependencies class itself (it must see interfaces to compose them)
- Leaf classes that genuinely work at the operation level (the phases themselves)
