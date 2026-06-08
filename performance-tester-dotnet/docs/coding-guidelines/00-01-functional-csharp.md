# 00-01. Applying FP to C#

> ⚙️ **Role:** Structural guideline. Translates the FP principles from **Guideline 00-00** into concrete C# and .NET code organization patterns. This is the bridge between the theory and the mechanical leaf rules.

This document is not a collection of independent rules to apply in isolation. The sections below form a **single cohesive architecture** built on three constrained pillars:

1. **A constrained architecture** — pure core + thin boundary adapters. Boundary adapters exist to bridge .NET's OOP world to our functional approach: inheriting from `BackgroundService`, implementing framework interfaces, registering services in the DI container. Domain logic is static functions — some pure, some impure — organized in modules. Side effects are not confined to the boundary; they happen wherever needed, but they are always **injected as delegates**, making them explicit in function signatures rather than hidden behind service interfaces.

2. **A constrained construction mechanism** — explicit wiring. Dependencies are delegates wired through module patterns and `BuildDependencies` methods. There are no magic service locators, no implicit resolution chains — every dependency is visibly connected at startup.

3. **A constrained testing philosophy** — test the core, de-emphasize the boundary. Pure functions are trivially testable with no mocking. Impure functions that call delegates can be tested by passing in test implementations. Thin boundary adapters are not unit-tested **because** they should remain mechanical wiring and orchestration — if you feel the need to unit-test an adapter, that is a signal that logic has crept in and should be extracted into a pure function. Integration tests cover the boundary, where the system under test is the entire C# project, exercising real wiring and real dependencies end-to-end.

### Scope and assumptions

These are non-negotiable premises that the rest of the document builds on:

- **Domain logic lives in static functions, not in framework classes.** A `BackgroundService` or `Controller` calls domain functions; it never contains domain logic itself.
- **The thin boundary adapter bridges .NET OOP to FP.** It exists to satisfy framework contracts (inheritance, interface implementation, DI registration). It may contain orchestration plumbing — driving a loop, holding a context, calling module functions and applying results — but never business decisions. All decisions come from pure functions in the module.
- **Constructors exist only in boundary adapters — for infrastructure services.** The pure core does not construct services, resolve dependencies, or manage lifecycle. Domain data construction is expected and encouraged: creating records, DU variants (`new HealthState.Degraded(...)`), Value Objects (`ConsecutiveCount.From(3)`), and collections are all normal.
- **Side effects are explicit, not confined.** I/O happens wherever needed — static functions can call RabbitMQ, databases, HTTP endpoints. The constraint is that side effects arrive as **delegate parameters**, making them visible in the function signature. A function that takes no delegates and returns a value is pure; a function that takes delegates is explicitly impure.
- **We accept losing some IDE navigability in exchange for explicit wiring.** "Go to implementation" on a delegate doesn't jump to the concrete function. This is a deliberate trade-off: wiring is visible in code, not hidden in a DI container's reflection-based resolution. Compensating techniques make this manageable in practice:
    - **Centralized wiring.** The answer to "what's wired to this delegate?" is always in one of two places: `BuildDependencies` or `ServiceCollectionExtensions.cs`. With interface-based DI, registrations can be scattered across multiple files and extension methods.
    - **Naming conventions.** Delegate names match function names — `StartProcessMonitoringDelegate` wraps `StartProcessMonitoring`. Search by name and you're there.
    - **Ctrl+Shift+F on the delegate type.** Finds the declaration, the wiring site, and all call sites in one search. This is often faster than "Go to Implementation" which only shows the target, not the wiring.
    - **Integration tests catch miswiring.** A delegate wired to the wrong function or left unregistered fails at test time, not silently at runtime.
- **Testing focuses on the pure core; boundary adapter tests are minimal.** Pure functions are trivially testable with no mocking — pass data in, assert data out. Thin boundary adapters are not unit-tested **because** they should remain mechanical wiring and orchestration. Needing to unit-test an adapter is a signal that logic has crept in and should be extracted into a pure function. Boundary adapters are covered by **integration tests where the system under test is the entire C# project**, exercising the real wiring, real dependencies, and real framework behavior end-to-end.

### Architecture overview

```
┌──────────────────────────────────────────────────────────────────────────┐
│  .NET Framework  (BackgroundService, Controller, DI Container)           │
│                                                                          │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │  Thin Boundary Adapter                                             │  │
│  │  Inherits framework classes, registers DI, no business decisions   │  │
│  │                                         ┌───────────────────────┐  │  │
│  │  ┌───────────────────────────────────┐  │ ServiceCollection     │  │  │
│  │  │  OrderProcessorService            │  │ Extensions            │  │  │
│  │  │  : BackgroundService              │  │                       │  │  │
│  │  │                                   │  │ Wires delegates to    │  │  │
│  │  │  Delegates immediately            │  │ concrete functions    │  │  │
│  │  │  to static functions ─────────┐   │  │ at startup            │  │  │
│  │  └───────────────────────────────│───┘  └───────────┬───────────┘  │  │
│  └──────────────────────────────────│──────────────────│──────────────┘  │
│                                     │                  │                 │
│  ┌──────────────────────────────────│──────────────────│──────────────┐  │
│  │  Module  (static class)          │    Dependencies ←┘              │  │
│  │                                  ▼    (record of delegates)        │  │
│  │                                                                    │  │
│  │  ┌──────────────────────┐    ┌──────────────────────────────────┐  │  │
│  │  │  Pure Functions      │    │  Impure Functions                │  │  │
│  │  │                      │    │                                  │  │  │
│  │  │  Transition()        │    │  ProcessBatch()                  │  │  │
│  │  │  Calculate()         │    │  PurgeQueues()                   │  │  │
│  │  │  Validate()          │    │                                  │  │  │
│  │  │                      │    │  Calls delegates ──────────────────────── → DB, MQ,
│  │  │  No delegates,       │    │  from Dependencies               │  │  │    HTTP,
│  │  │  no side effects     │    │  for all I/O                     │  │  │    File I/O
│  │  └──────────────────────┘    └──────────────────────────────────┘  │  │
│  └────────────────────────────────────────────────────────────────────┘  │
│                                                                          │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │  Api.cs  (delegates, data types — the module's public surface)     │  │
│  └────────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────────┘
```

### Testing strategy

| Test type                          | What it covers                       | How                                                                 |
| ---------------------------------- | ------------------------------------ | ------------------------------------------------------------------- |
| Unit tests (no mocking)            | Pure functions                       | Data in → data out. `Transition(state, event) → new state`          |
| Unit tests (delegate substitution) | Impure functions                     | Pass test delegates, assert calls and results                       |
| Integration tests (entire project) | Adapter + wiring + real dependencies | Whole C# project as SUT, real framework behavior end-to-end         |
| N/A                                | Thin boundary adapters               | Not tested directly — covered implicitly by integration tests above |

---

## 1. The .NET Boundary — Thin Boundary Adapters

We build on the .NET framework. Code that must interact with it follows its rules — but this interaction should be a **thin boundary adapter**, not something that leaks into business logic.

Boundary code includes: implementing framework interfaces, inheriting from framework classes (`BackgroundService`, `ControllerBase`), catching exceptions that the framework throws for expected conditions, and registering services in the DI container.

> 📚 **FP grounding — two distinct concepts:**
>
> **The impure–pure–impure sandwich** is a _coding pattern_ from FP (sometimes called the "functional sandwich" or Mark Seemann's "impureim sandwich"). It appears at the function level: gather inputs (impure), make a decision (pure), act on the result (impure). Any function — a `BackgroundService` loop, a static orchestration function, a simple helper — can structure itself this way. This is not an architectural layer; it is a technique applied wherever you write code.
>
> **The thin boundary adapter** is an _architectural role_ inspired by Gary Bernhardt's Functional Core, Imperative Shell. In Haskell, the `IO` monad enforces the pure/impure split at the type system level. In C#, we enforce it by convention: boundary classes are thin adapters that satisfy framework contracts and delegate immediately to static functions. Those static functions may themselves be pure or impure (calling injected delegates for I/O), but the boundary adapter contains no business decision logic — only framework plumbing and orchestration (loops, context ownership, calling module functions).
>
> The key distinction: our architecture does **not** confine all I/O to the boundary adapter. Impure module functions perform I/O freely — the constraint is that effects arrive as **delegate parameters**, making them visible in function signatures. The boundary adapter's job is to satisfy .NET, not to be the only place where side effects happen.

```csharp
// ✅ Thin boundary — inherits from BackgroundService because the framework requires it,
// but immediately delegates to pure static functions
public sealed class OrderProcessorService(OrderProcessorModule.Dependencies deps) : BackgroundService
{
    private OrderProcessorContext _context = OrderProcessorContext.Initial;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var (newContext, action) = OrderProcessorModule.ProcessNext(_context, await deps.FetchNextOrder());
            _context = newContext;
            await action(deps);
        }
    }
}
```

**Boundary classes SHOULD NOT check for nulls in constructor parameters.** We omit null guards in adapter constructors by policy because composition is verified elsewhere: correct wiring is validated by CI composition/integration tests. If something is miswired, failure is expected during integration checks, not handled with defensive null logic at runtime. This rule assumes adapters are only constructed via DI composition and validated by integration tests; if an adapter can be constructed manually (e.g., in tools or other hosts), guard accordingly.

```csharp
// ✅ No null checks — wiring errors are caught by integration tests
public sealed class OrderProcessorService(OrderProcessorModule.Dependencies deps) : BackgroundService { ... }

// ❌ Omitted by policy — composition is verified by integration tests, not by runtime guards
public sealed class OrderProcessorService : BackgroundService
{
    private readonly OrderProcessorModule.Dependencies _deps;
    public OrderProcessorService(OrderProcessorModule.Dependencies deps)
    {
        _deps = deps ?? throw new ArgumentNullException(nameof(deps));
    }
}
```

**Constructor parameters should very rarely be nullable, if ever.** A nullable constructor parameter signals that the dependency is optional, which almost never reflects reality. If a class needs something to function, make it non-nullable and treat a missing registration as a composition error caught at test time.

### Exception handling policy

Exceptions cross two levels, each with a different policy:

**Module functions: catch only what you can meaningfully handle.** Network timeouts, connection refused, HTTP 503, broker unavailable — these are expected operational conditions where the module can do something useful: retry, reconnect, return a `Result` failure, or transition a state machine. Catch them by type (`IOException`, `HttpRequestException`, `OperationCanceledException`) and convert them to data. This is the principle from John Ousterhout's _A Philosophy of Software Design_: **define errors out of existence**. The health monitor example later in this document demonstrates this — HTTP failures and timeouts are not exceptional, they are normal transitions that the state machine handles by tracking degraded state and recovery.

But not every exception deserves handling. If a file write fails because of insufficient permissions, if an external library throws a `NullReferenceException` from its internals, if a database connection can't be established because the server is gone — there is nothing meaningful the module can do. Let these propagate. A boundary adapter or the host will catch them for observability. The worst outcome is catching bare `Exception` and wrapping a genuine bug inside a `CheckFailed` event where it will never be found. Catch _specific types_ where you have a _specific recovery strategy_; let everything else bubble up.

```csharp
// ✅ Catch specific infrastructure failures — convert to events
private static async Task<HealthEvent> CheckAsync(
    Uri endpoint, CancellationToken ct, CheckHealthDelegate checkHealth, LogDelegate log)
{
    try
    {
        var result = await checkHealth(endpoint, ct);
        return result switch
        {
            Result<Unit, string>.Success => new HealthEvent.CheckSucceeded(),
            Result<Unit, string>.Failure f => new HealthEvent.CheckFailed(f.Error),
            _ => throw new InvalidOperationException("Unreachable")
        };
    }
    catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
    {
        log($"Health check error: {ex.Message}");
        return new HealthEvent.CheckFailed(ex.Message);
    }
    // NullReferenceException, InvalidCastException, etc. propagate — they are bugs
}
```

**Boundary adapters: optional safety net for observability.** Host behavior for unhandled exceptions in `BackgroundService.ExecuteAsync` varies by .NET version and configuration — don't rely on framework defaults for crash semantics. A last-resort `catch (Exception)` at this level is acceptable as an explicit policy to ensure observability — but only to log and transition to a terminal state, never to silently continue:

```csharp
// ✅ Safety net at boundary — log, transition to terminal, stop
protected override async Task ExecuteAsync(CancellationToken ct)
{
    try
    {
        while (!ct.IsCancellationRequested)
        {
            // ... normal loop calling module functions
        }
    }
    catch (Exception ex) when (!ct.IsCancellationRequested)
    {
        // Unexpected bug — log and stop. Do not retry.
        _logger.LogCritical(ex, "Unhandled exception in background service");
    }
}
```

The two levels are complementary: module functions handle expected failures close to the source (retry, reconnect, return Result); boundary adapters catch the unexpected as a safety net for observability.

---

## 2. Composition Over Inheritance

We do not use inheritance for code reuse. Inheritance exists only to satisfy framework contracts at the boundary (e.g., inheriting `BackgroundService`, implementing `IHostedService`). Reuse is achieved by combining functions.

> 📚 **FP grounding:** Functional languages don't have inheritance at all. Haskell achieves polymorphism through **type classes**, F# through **module functions and composition operators** (`>>`, `|>`), and Rust through **traits**. Code reuse comes from combining small functions, not from hierarchies. Higher-order functions (00-00 section 10) are the primary reuse mechanism.

```csharp
// ✅ Composition — reuse via function parameters
public static RetryResult RetryWithBackoff(
    Func<Task<bool>> operation,
    RetryPolicy policy) => ...

// ❌ Inheritance — reuse via base class
public abstract class RetryableService
{
    protected async Task<bool> RetryWithBackoff(Func<Task<bool>> operation) => ...
}
public class OrderService : RetryableService { ... }
```

**Therefore:**

- **Most code should be static functions.** Instance methods exist only at the boundary where the framework demands them.
- **All classes should be `sealed`** unless explicitly designed for inheritance (which should be almost never). If we don't design for inheritance, we forbid it. Note that `sealed` prevents others from inheriting _from your class_ — it does not prevent your class from inheriting a framework base class. A boundary adapter is `sealed` _and_ inherits from `BackgroundService`; both are correct.
- **Record inheritance is valid for data.** Records hold data, not reusable behavior — inheriting one record from another is a structural relationship, not code reuse. Dunet unions rely on this: variant records inherit from the union's base record. This is fundamentally different from class inheritance for behavior sharing, which we avoid.

```csharp
// ✅ Sealed and inheriting from framework class — sealed prevents further subclassing, not framework inheritance
public sealed class OrderProcessorService(OrderProcessorModule.Dependencies deps) : BackgroundService { ... }

// ✅ Static — cannot be inherited by definition
public static class OrderProcessorModule { ... }

// ✅ Record inheritance is valid — records hold data, not reusable behavior
public record Order(OrderId Id, ImmutableList<OrderLine> Lines);

// ❌ Unsealed class without a reason
public class OrderProcessorService : BackgroundService { ... }
```

---

## 3. Discriminated Unions with Dunet

C# lacks native discriminated unions. We use [**Dunet**](https://github.com/domn1995/dunet), a source generator that provides true exhaustive matching at compile time.

A Dunet union is declared as a `partial record` with the `[Union]` attribute. Each variant is a nested `partial record`.

```csharp
using Dunet;

// ✅ Dunet union — source generator creates Match(), MatchAsync(), implicit conversions
[Union]
partial record PaymentMethod
{
    partial record CreditCard(CardNumber Number, ExpiryDate Expiry);
    partial record BankTransfer(Iban Iban);
    partial record CryptoWallet(WalletAddress Address);
}

// ✅ Generic unions
[Union]
partial record Option<T>
{
    partial record Some(T Value);
    partial record None;
}
```

### Exhaustive `Match()` — not `switch`

Dunet generates a `Match()` method whose parameter list **is** the set of variants. Adding a new variant to the union causes a compile **error** at every `Match()` call site that doesn't handle it. This is stronger than a `switch` with `_`, which silently swallows new variants.

```csharp
// ✅ Dunet Match — exhaustive by construction, no default branch exists
public static Fee CalculateFee(PaymentMethod method) => method.Match(
    creditCard => creditCard.Number.IsAmex ? Fee.High : Fee.Standard,
    bankTransfer => Fee.Low,
    cryptoWallet => Fee.Variable
);

// ✅ Combining Result + Dunet failure matching
var message = PlaceOrder(order, inventory) switch
{
    { IsSuccess: true, Value: var confirmation } => $"Order {confirmation.Id} confirmed",
    { Failure: var failure } => failure.Match(
        emptyOrder => "Order has no lines",
        insufficientStock => $"Out of stock: {string.Join(", ", insufficientStock.Products)}"
    ),
};

// ❌ Using switch instead of Match() — loses compile-time exhaustiveness
public static Fee CalculateFee(PaymentMethod method) => method switch
{
    PaymentMethod.CreditCard => Fee.Standard,
    _ => Fee.Low, // silently handles any new variant as Low
};
```

**Use `Match()` for Dunet unions, `switch` for everything else** (enums, strings, primitives, types from external libraries). See 00-00 section 7 for `switch` expression guidelines.

> **Exception: state machine transitions.** State machines (section 10) may use `switch` with tuple patterns `(state, event)` because nested `Match()` lambdas harm readability. This exception is acceptable **only if the transition lists all relevant state×event combinations explicitly** — no top-level `_ => current` catch-all that silently swallows future states. A scoped `_ =>` _inside a specific state arm_ is fine when justified with a comment — e.g., a terminal state that ignores all events: `(Stopped, _) => current // terminal — no transitions out`.
>
> **Trade-off: adding new variants.** With `Match()`, adding a new DU variant produces compile errors at every call site that doesn't handle it. With `switch` on tuple patterns, the compiler cannot help — you rely on review discipline to spot missing combinations when new states or events are introduced. This is an acceptable trade-off for readability, but the team must be aware of it.

---

## 4. Dependency Injection via Delegates

Dependencies are injected as **delegates**, not as direct references to static classes or their methods. This keeps the pure core decoupled from the boundary adapters.

> 📚 **FP grounding:** In FP languages, dependency injection doesn't exist as a pattern — it's just **passing functions as arguments**. Haskell naturally passes `IO` actions as parameters; F# passes function values. The OOP world invented interfaces and DI containers to achieve what higher-order functions give you for free. Named delegates are C#'s closest equivalent to Haskell's type aliases for function signatures (`type CheckStock = ProductId -> Warehouse -> IO StockLevel`).

For each function that needs to be available via DI, the module exposes a **delegate with the same name and a `Delegate` suffix**.

**The delegate does not necessarily have the same parameters as the function**, because some parameters may be baked in at startup (see section 6).

```csharp
// A static function declared somewhere — the actual implementation
public static async Task<StockLevel> CheckStock(ProductId productId, Warehouse warehouse) => ...

// A delegate for it — same name + "Delegate" suffix
public delegate Task<StockLevel> CheckStockDelegate(ProductId productId, Warehouse warehouse);

// Another function receives the delegate — DI without interfaces
public static async Task<Result<OrderConfirmation, OrderFailure>> PlaceOrder(
    CheckStockDelegate checkStock,  // injected behavior
    Order order)                    // runtime data
{
    var stock = await checkStock(order.ProductId, order.Warehouse);
    // ... pure business logic using the stock level
}
```

**Key points:**

- The caller of `PlaceOrder` decides _which_ `CheckStock` implementation to pass — the function itself is decoupled from the concrete implementation.
- The `delegate` exposes only runtime parameters — startup values can be baked in at wiring time (see section 6).
- Named delegates are self-documenting: `CheckStockDelegate` communicates intent better than `Func<ProductId, Warehouse, Task<StockLevel>>`.

---

## 5. The Module Pattern

A `static class` in this codebase is not just a bag of utility methods — it is a **module**: a cohesive container for related data types, behavior, and their public surface area.

> 📚 **FP grounding:** This maps directly to **Haskell's module system** and **F#'s modules** — a namespace-like unit that groups related types and functions, with explicit control over what is exported (public) and what is internal. The `XyzModule` naming convention makes this lineage explicit.

A module named `XyzModule` contains:

- **Public static methods** — the behavior
- **Records** — the data types used as parameters and return values
- **Delegates** with `Delegate` suffix — the public API for DI consumers (see section 4)
- **A `Dependencies` record** — when the module needs multiple injected behaviors
- **A `BuildDependencies` method** — to wire up the dependencies with baked-in startup values

```csharp
internal static class ProcessMonitoringModule
{
    // --- Data ---
    public record MonitoringResult(ProcessId ProcessId, string Message);

    // --- Delegate: the public API. Notice CancellationToken is NOT a parameter ---
    public delegate Task<Result<MonitoringResult, string>> StartProcessMonitoringDelegate(ProcessId processId);

    // --- Dependencies record: groups all delegates the module needs ---
    // (Analogous to Haskell's Reader pattern — a read-only
    // environment of capabilities threaded through computations)
    public record Dependencies(StartProcessMonitoringDelegate StartProcessMonitoring);

    // --- BuildDependencies: bakes startup values into the delegates ---
    public static Dependencies BuildDependencies(
        IServiceProvider services,
        CancellationToken ct)
    {
        // CancellationToken is a startup value — baked in here, invisible to callers
        return new Dependencies(
            StartProcessMonitoring: pid => StartProcessMonitoring(pid, ct));
    }

    // --- Public entry point: receives Dependencies + runtime values ---
    public static Task<Result<MonitoringResult, string>> Start(
        Dependencies dependencies,
        ProcessId processId) =>
        dependencies.StartProcessMonitoring(processId);

    // --- Private implementation: has the full parameter list including startup values ---
    private static async Task<Result<MonitoringResult, string>> StartProcessMonitoring(
        ProcessId processId,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(100, ct);
            return new MonitoringResult(processId, $"Process {processId} monitoring started successfully.");
        }
        catch (OperationCanceledException)
        {
            return $"Process {processId} monitoring was cancelled.";
        }
    }
}
```

---

## 6. Startup Values vs. Runtime Values

There is a fundamental difference between values known at startup and values that arrive at runtime.

**Startup values** are resolved once and baked into delegates so callers don't need to pass them. These include: configuration from `appsettings.json` or CLI arguments, `ILogger` instances, `CancellationToken` from the host, connection strings, DI-resolved services.

**Runtime values** remain as function parameters. These include: data from incoming requests or messages, user input, values that can change during execution (e.g., dynamically reloadable configuration).

**Reloadable configuration is a special case.** Baking `IOptions<T>.Value` freezes the config snapshot at startup — changes to `appsettings.json` at runtime won't be seen. If config reload matters, bake a function that reads the current value instead:

```csharp
// ✅ Reloadable: capture the monitor, read current value each call
var monitor = sp.GetRequiredService<IOptionsMonitor<SmtpConfig>>();
return new Dependencies(
    SendNotification: (recipient, msg) => SendNotification(monitor.CurrentValue, recipient, msg));

// ❌ Frozen: baked once at startup, never updates
var config = sp.GetRequiredService<IOptions<SmtpConfig>>().Value;
return new Dependencies(
    SendNotification: (recipient, msg) => SendNotification(config, recipient, msg));
```

### Baking in startup values

**Approach 1 — `Dependencies` record** (when a module has multiple dependencies):

The `ProcessMonitoringModule` in section 5 demonstrates this pattern. The key is the `BuildDependencies` method — it receives startup values and captures them in lambdas:

```csharp
// Inside the module: the private function needs a CancellationToken (startup value)
private static async Task<Result<MonitoringResult, string>> StartProcessMonitoring(
    ProcessId processId,       // runtime value — stays as parameter
    CancellationToken ct)      // startup value — will be baked in
{ ... }

// BuildDependencies receives startup values and bakes them into the delegate
public static Dependencies BuildDependencies(
    IServiceProvider services,
    CancellationToken ct)      // startup value received here
{
    return new Dependencies(
        // The lambda captures `ct` — callers only pass runtime values
        StartProcessMonitoring: pid => StartProcessMonitoring(pid, ct));
}

// The delegate the caller sees — CancellationToken is invisible
public delegate Task<Result<MonitoringResult, string>> StartProcessMonitoringDelegate(ProcessId processId);
```

**Approach 2 — Direct wiring in `ServiceCollectionExtensions`** (when a function has just 1-2 parameters to bake in):

```csharp
// Bake SmtpConfig directly into the delegate registration
services.AddSingleton<NotificationModule.SendNotificationDelegate>(sp =>
{
    var config = sp.GetRequiredService<IOptions<SmtpConfig>>().Value;
    return (recipient, message) => NotificationModule.SendNotification(config, recipient, message);
});
```

**`CancellationToken` — startup or runtime?** It depends on lifetime. The same type serves two different purposes:

**Host-level stop token — startup value, bake it in.** A `BackgroundService` receives a single token that signals application shutdown. It exists once and lives for the host's lifetime — bake it into delegates so every call respects graceful shutdown without passing it around:

```csharp
// BackgroundService scenario: one token for the lifetime of the host
public static Dependencies BuildDependencies(IServiceProvider sp, CancellationToken hostCt)
{
    var httpClient = sp.GetRequiredService<HttpClient>();

    return new Dependencies(
        // hostCt baked in — every call automatically respects shutdown
        CheckHealth: () => CheckHealth(httpClient, hostCt));
}
```

**Per-request cancellation token — runtime value, pass it through.** An API controller receives a different token per HTTP request — it dies when the client disconnects. This varies per call, so it stays as a delegate parameter:

```csharp
// API scenario: each request has its own token
public static Dependencies BuildDependencies(IServiceProvider sp)
{
    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

    return new Dependencies(
        // requestCt stays as parameter — it varies per call
        ProcessOrder: async (order, requestCt) =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await ProcessOrder(order, db, requestCt);
        });
}
```

---

## 7. Lifecycle Management — Delegates and Service Lifetimes

`BuildDependencies` runs once at startup and captures services into closures that live for the lifetime of the host. This is a direct consequence of baking startup values into delegates (section 6) — and it creates a correctness hazard if the captured service has a shorter lifetime than the delegate.

**Singleton-lifetime services are safe to capture.** `IHttpClientFactory`, configuration objects, loggers — these are designed to live for the duration of the application.

**Scoped services are dangerous to capture.** A `DbContext` captured at startup outlives its intended scope: connections leak, tracked entities grow stale, and concurrent calls share state they shouldn't. The same applies to any service registered with `AddScoped` or `AddTransient`.

The fix: never capture a scoped service directly. Capture `IServiceScopeFactory` (which is itself a singleton) and create a scope inside the delegate each time it executes:

```csharp
public static Dependencies BuildDependencies(IServiceProvider sp, CancellationToken ct)
{
    // ✅ Safe: IServiceScopeFactory is a singleton — safe to hold in a long-lived closure
    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    var logger = sp.GetRequiredService<ILogger<OrderProcessorService>>();

    return new Dependencies(
        FetchNextOrder: async () =>
        {
            // ✅ Safe: scope is created and disposed per call — DbContext lives only for this operation
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Orders.FirstOrDefaultAsync(o => !o.Processed, ct);
        },
        Log: msg => logger.LogInformation("{Message}", msg));
}
```

```csharp
// ❌ Dangerous: DbContext is scoped — captured once, held forever
public static Dependencies BuildDependencies(IServiceProvider sp, CancellationToken ct)
{
    var db = sp.GetRequiredService<AppDbContext>(); // scoped service resolved at startup
    var logger = sp.GetRequiredService<ILogger<OrderProcessorService>>();

    return new Dependencies(
        FetchNextOrder: async () =>
        {
            // This DbContext is shared across every call for the lifetime of the host.
            // Connections leak, change tracking accumulates, concurrency bugs appear.
            return await db.Orders.FirstOrDefaultAsync(o => !o.Processed, ct);
        },
        Log: msg => logger.LogInformation("{Message}", msg));
}
```

**Rule of thumb:** if the service is registered as `AddScoped` or `AddTransient`, don't capture it — capture the factory and resolve per-call. If it's `AddSingleton`, capturing it directly is fine.

---

## 8. Vertical Slice — Project Structure

When a project is a **vertical slice** exposing some functionality, it follows this structure:

```
MySlice/
├── Api.cs                          // Public contract — delegates, records, Value Objects, enums
├── ServiceCollectionExtensions.cs  // AddMySlice() extension method — wiring
└── Internal/                       // Everything else — all internal or private
    ├── Processing/
    │   └── OrderProcessorModule.cs
    ├── Pricing/
    │   └── OrderPricingModule.cs
    └── ...
```

### `Api.cs`

The public contract of the slice. Contains **only** the types consumers need: delegates, records, Value Objects, enums, and DU types. No logic.

> 📚 **FP grounding:** This is the C# equivalent of **F#'s `.fsi` signature files** — a declaration of what the module exports, separated from the implementation. In Haskell, this corresponds to the **module export list** (`module Foo (bar, baz) where ...`) that explicitly controls visibility.

```csharp
// Api.cs — the public contract of this slice
namespace MySlice;

public delegate Task<OrderConfirmation> PlaceOrderDelegate(Order order);
public delegate Task<Option<Order>> GetOrderDelegate(OrderId id);

public record Order(OrderId Id, ImmutableList<OrderLine> Lines);
public record OrderConfirmation(OrderId Id, DateTimeOffset ConfirmedAt);
```

### `ServiceCollectionExtensions.cs`

The single entry point for consumers. Wires internal modules and exposes functionality only through the public types defined in `Api.cs`.

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMySlice(this IServiceCollection services)
    {
        services.AddSingleton<PlaceOrderDelegate>(sp =>
        {
            var deps = OrderProcessorModule.BuildDependencies(sp);
            return order => OrderProcessorModule.PlaceOrder(deps, order);
        });
        return services;
    }
}
```

### `Internal/`

**All code in this folder must be `internal` or `private`.** Nothing inside `Internal/` is part of the slice's public API. Consumers only see what's in `Api.cs`.

---

## 9. The ToolBox Pattern

When a function is needed by more than one module, the **ToolBox** pattern applies.

A `static class` named `XyzToolBox` lives in the **most inner common folder** of the modules that use it. **ToolBox functions must be pure** — no delegates, no I/O, no side effects. If a shared operation needs to perform I/O (clearing a database, purging a queue, calling a third-party service), it belongs in its own Module with proper delegates and `BuildDependencies` — not in a ToolBox. `BuildDependencies` methods and `ServiceCollectionExtensions` can reference ToolBox functions for wiring and baking in.

```
Internal/
├── Processing/
│   ├── OrderProcessorModule.cs     // uses RetryToolBox
│   ├── PaymentProcessorModule.cs   // uses RetryToolBox
│   └── RetryToolBox.cs             // lives in the most inner common folder
└── Pricing/
    └── OrderPricingModule.cs
```

```csharp
internal static class RetryToolBox
{
    internal static async Task<Result<T, RetryFailure>> WithRetry<T>(
        Func<Task<Result<T, RetryFailure>>> operation,
        RetryPolicy policy) => ...
}
```

---

## 10. State Machines

Represent state as a discriminated union, events as another discriminated union, and transitions as a **single pure function**: given a current state and an event, return a new state. The entire state machine is visible in one place.

```csharp
public static class HealthCheckModule
{
    [Union]
    partial record HealthState
    {
        public partial record Healthy;
        public partial record Unhealthy(string Reason);
        public partial record Stopped; // terminal
    }

    [Union]
    partial record HealthEvent
    {
        public partial record CheckPassed;
        public partial record CheckFailed(string Reason);
        public partial record StopRequested;
    }

    // Pure: depends only on inputs. No mutation, no side effects.
    // State machine exception: tuple switch is used for readability (explicit state×event).
    public static HealthState Transition(HealthState current, HealthEvent @event)
        => (current, @event) switch
        {
            (HealthState.Healthy, HealthEvent.CheckPassed)       => current,
            (HealthState.Healthy, HealthEvent.CheckFailed e)     => new HealthState.Unhealthy(e.Reason),
            (HealthState.Healthy, HealthEvent.StopRequested)     => new HealthState.Stopped(),

            (HealthState.Unhealthy, HealthEvent.CheckPassed)     => new HealthState.Healthy(),
            (HealthState.Unhealthy, HealthEvent.CheckFailed e)   => new HealthState.Unhealthy(e.Reason),
            (HealthState.Unhealthy, HealthEvent.StopRequested)   => new HealthState.Stopped(),

            (HealthState.Stopped, _)                             => current, // terminal
        };
}
```

**Key points:**

- **One `Transition` function, all cases.** The entire state machine is visible in a single pattern match — easy to reason about, easy to review, easy to test.
- **The transition function is pure.** It takes the current state and an event. It returns a new state. No mutation, no side effects — trivially testable.
- **Missing behavior stays visible.** No top-level `_ => current` catch-all. The transition is centralized, so introducing new states/events makes unhandled behavior obvious during review. `_ => …` and `=> current` are allowed only **inside a specific state arm** and only with a comment explaining why (idempotent, not applicable, terminal, etc.).
- **Terminal states are explicit.** `Stopped` is terminal — the boundary checks for it and exits. The state machine doesn't throw exceptions; it returns data.

---

## 11. State Management — The Context Pattern

For stateful processes, we use a **Context record** to hold the state. The record has a `Context` suffix and is **owned by a single caller** — a boundary class (e.g., `BackgroundService`) or an impure static function managing a workflow. The owner is the only code that reads and writes the context's mutable properties.

> ⚠️ **This is the deliberate exception to immutability.** We mutate context for practical reasons: it owns long-lived resources (connections, buffers) and evolving state in long-running workflows, simplifying orchestration code by keeping state as a single owned object. Mutation is quarantined to the owner — pure functions (like `Transition`) never see or touch the context; they take values in and return values out. The owner assigns the result.
>
> **The context must not be shared across threads.** Single ownership means single thread. If you need concurrent access, introduce synchronization or redesign around message passing.
>
> **Only workflow state is mutable — domain entities stay immutable.** The context holds operational bookkeeping: connection state, buffers, timestamps, attempt counters. Domain records, Value Objects, and DU variants remain immutable throughout.
>
> **If the context holds references to disposable resources** (e.g., a connection wrapping a socket or database handle), the owner is responsible for disposal when transitioning away. Reset the reference _and_ dispose the underlying resource — resetting to a no-op alone leaks. A common pattern is a `DisconnectDelegate` or `DisposeDelegate` that the boundary calls on every disconnect or terminal transition.

> 📚 **FP grounding:** This is the C# adaptation of Haskell's **State monad** (`s -> (a, s)`) — a computation that receives the current state, performs work, and produces a result alongside the updated state. In Haskell, the State monad returns a new state value; in C#, the owner handles reassignment. The `Dependencies` record (sections 4-5) is the companion concept, analogous to Haskell's **Reader pattern** (`r -> a`) — a read-only environment threaded through computations. Together, they cover the two most common ways FP languages manage context: Reader for static configuration, State for evolving state.

Continuing the health check state machine from section 10, this is how a `BackgroundService` drives the loop. The module infrastructure (delegates, `Dependencies` record, `BuildDependencies`) is omitted here to keep focus on the pattern — the full example later in the document shows all the wiring.

```csharp
// The boundary adapter: thin class that drives the state machine loop
public sealed class HealthCheckService(
    HealthCheckModule.Dependencies deps) : BackgroundService
{
    private HealthState _state = new HealthState.Healthy();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // 1) Impure: poll the endpoint
            var @event = await deps.CheckHealth();

            // 2) Pure: transition
            var previous = _state;
            _state = HealthCheckModule.Transition(_state, @event);

            // 3) Impure: act on state change
            if (previous is HealthState.Healthy && _state is HealthState.Unhealthy u)
                deps.Log($"Service went unhealthy: {u.Reason}");
            if (previous is HealthState.Unhealthy && _state is HealthState.Healthy)
                deps.Log("Service recovered.");

            if (_state is HealthState.Stopped)
                return;

            await Task.Delay(deps.Interval, ct);
        }
    }
}
```

**Mutation is quarantined to a single owner.** The code that creates the context is the only code that mutates it — whether that's a `BackgroundService` driving a loop, or an impure static function managing its own workflow state. Pure functions (like `Transition`) never see the context; they take individual values as parameters and return new values. The owner calls the pure function, then assigns the result to the context.

---

## 12. Parse, Don't Validate — Value Objects

Avoid primitive obsession. Instead of passing raw `int`, `string`, or `double` around, **parse values into Value Objects at the boundary** and use the Value Object from then on.

> 📚 **FP grounding:** This principle comes from Alexis King's influential Haskell article [_Parse, Don't Validate_](https://lexi-lambda.github.io/blog/2019/11/05/parse-don-t-validate/) — the idea that parsing (converting unstructured data into structured, type-safe representations) should happen once at the boundary, and from that point on the type system carries the proof that the data is valid. In Haskell, this is achieved with `newtype` wrappers and smart constructors.

We use [**Vogen**](https://github.com/SteveDunn/Vogen) for all Value Objects. Vogen is a source generator that produces Value Objects with built-in converters for System.Text.Json, EF Core, Dapper, Newtonsoft.Json, and more — solving the traditional hurdles of **JSON (de)serialization** and **database persistence** with near-zero runtime overhead compared to raw primitives.

```csharp
[ValueObject<int>]
public partial struct Port
{
    // 0 is valid: OS assigns an ephemeral port. Use 1..65535 if your domain forbids it.
    private static Validation Validate(int value) =>
        value is >= 0 and <= 65535
            ? Validation.Ok
            : Validation.Invalid($"Port must be between 0 and 65535, got {value}");
}

[ValueObject<string>]
public partial struct DatabaseName
{
    private static Validation Validate(string value) =>
        !string.IsNullOrWhiteSpace(value)
            ? Validation.Ok
            : Validation.Invalid("Database name must not be empty");
}

[ValueObject<string>]
public partial struct HostUrl
{
    private static Validation Validate(string value) =>
        value.StartsWith("http://") || value.StartsWith("https://")
            ? Validation.Ok
            : Validation.Invalid("Host URL must start with http:// or https://");
}

// Usage — parse at the boundary, pass Value Objects everywhere
var port = Port.From(8080);           // validated, serialization-ready
var db = DatabaseName.From("mydb");   // System.Text.Json, EF Core, Dapper all work out of the box

// ✅ Functions declare what they actually need — self-documenting, compiler-enforced
public static ConnectionString BuildConnectionString(HostUrl host, Port port, DatabaseName db) => ...

// ❌ Primitive obsession — easy to swap arguments, no validation guarantee
public static string BuildConnectionString(string host, int port, string db) => ...
```

**Why this matters:**

- **Compiler-enforced correctness.** You cannot accidentally pass a `Port` where a `MaxRetries` is expected, even though both wrap `int`.
- **Self-documenting APIs.** `BuildConnectionString(HostUrl, Port, DatabaseName)` is unambiguous. `BuildConnectionString(string, int, string)` is a bug waiting to happen.
- **Parse once, trust everywhere.** Once a `Port` exists, every function that receives it knows the value is between 0 and 65535. No redundant checks scattered across the codebase.
- **Parse at the boundary.** Raw input (CLI args, HTTP requests, config files) is parsed into Value Objects at the edge. From that point inward, the code works exclusively with valid, typed values.

**Vogen provides:**

- **Analyzers** that prevent `new Port()` or `default(Port)` — you can't create an invalid instance.
- **Generated converters** for System.Text.Json, EF Core, Dapper, Newtonsoft, BSON, and more — configured via `Conversions` flags.
- **Near-zero overhead** — benchmarks show nearly identical performance to raw primitives.
- **`TryFrom()`** for Result-style creation without exceptions.

### Shared constraint functions with the ToolBox pattern

> 📚 **FP grounding:** This is the C# equivalent of Haskell's **smart constructor** pattern, where a `newtype` has a hidden data constructor and a public function that validates before constructing. The ToolBox is analogous to a shared library of validation combinators that smart constructors compose.

Many Value Objects share the same underlying constraint. Instead of repeating the same validation logic, define a **`ValueObjectToolBox`** with reusable validation functions that Vogen's `Validate` methods call:

```csharp
// Shared validation functions — pure, composable, reusable across Value Objects
public static class ValueObjectToolBox
{
    public static Validation ValidateNonNegativeInt(int value) =>
        value >= 0
            ? Validation.Ok
            : Validation.Invalid($"Value must be non-negative, got {value}");

    public static Validation ValidateNonNegativeDouble(double value) =>
        value >= 0
            ? Validation.Ok
            : Validation.Invalid($"Value must be non-negative, got {value}");

    public static Validation ValidateNonEmptyString(string value) =>
        !string.IsNullOrWhiteSpace(value)
            ? Validation.Ok
            : Validation.Invalid("Value must not be empty");

    public static Validation ValidateRange(int value, int min, int max) =>
        value >= min && value <= max
            ? Validation.Ok
            : Validation.Invalid($"Value must be between {min} and {max}, got {value}");
}

// Value Objects compose ToolBox functions — minimal boilerplate
[ValueObject<int>]
public partial struct WorkersCount
{
    private static Validation Validate(int value) => ValueObjectToolBox.ValidateNonNegativeInt(value);
}

[ValueObject<int>]
public partial struct Port
{
    private static Validation Validate(int value) => ValueObjectToolBox.ValidateRange(value, 0, 65535);
}

[ValueObject<string>]
public partial struct DatabaseName
{
    private static Validation Validate(string value) => ValueObjectToolBox.ValidateNonEmptyString(value);
}
```

### Parsing from different types

Vogen's `From()` only accepts the underlying primitive type. When a Value Object needs to be parsed from a different type (e.g., a `string` from a CLI argument into an `int`-based Value Object), add a static `Parse` method to the partial struct:

```csharp
[ValueObject<int>]
public partial struct Port
{
    private static Validation Validate(int value) => ValueObjectToolBox.ValidateRange(value, 0, 65535);

    // Parse from string — boundary code that receives raw text
    public static Result<Port, string> Parse(string value) =>
        int.TryParse(value, out var parsed)
            ? TryFrom(parsed) switch
            {
                { IsSuccess: true, ValueObject: var port } => port,
                { Error.ErrorMessage: var msg } => Result.Fail<Port, string>(msg),
            }
            : $"Cannot parse '{value}' as a port number";
}

// At the boundary — CLI args arrive as strings
var portResult = Port.Parse(args["--port"]);
```

This keeps Vogen as the single source of truth for the constraint. The `Parse` method handles the type conversion, then delegates to `TryFrom()` for validation.

### Chaining multiple parses — Railway-Oriented Programming via LINQ

When parsing multiple values at the boundary, each returning `Result<T, TFailure>`, we use **LINQ query syntax** to chain them into a pipeline that short-circuits on first failure (see 00-00 section 6 for the concept).

This is enabled by two extension methods on `Result<T, TError>` — `Select` and `SelectMany` — that let the C# compiler interpret `from` / `select` as monadic bind:

```csharp
public static class ResultLinqExtensions
{
    /// Projects the success value. Enables the <c>select</c> keyword.
    public static Result<U, TError> Select<T, TError, U>(
        this Result<T, TError> result,
        Func<T, U> selector)
    {
        if (result is Result<T, TError>.Success s)
            return new Result<U, TError>.Success(selector(s.Value));

        return new Result<U, TError>.Failure(((Result<T, TError>.Failure)result).Error);
    }

    /// Chains a dependent operation. Enables multiple <c>from</c> clauses.
    public static Result<V, TError> SelectMany<T, TError, U, V>(
        this Result<T, TError> result,
        Func<T, Result<U, TError>> bind,
        Func<T, U, V> project)
    {
        if (result is not Result<T, TError>.Success s)
            return new Result<V, TError>.Failure(((Result<T, TError>.Failure)result).Error);

        var bound = bind(s.Value);
        if (bound is Result<U, TError>.Success next)
            return new Result<V, TError>.Success(project(s.Value, next.Value));

        return new Result<V, TError>.Failure(((Result<U, TError>.Failure)bound).Error);
    }
}
```

With these in place, parsing multiple inputs reads as a flat pipeline:

```csharp
var parseResult =
    from eventCount in EventCount.Create(context.ParseResult.GetValueForOption(eventsOption))
    from apiWorkers in WorkerCount.Create(context.ParseResult.GetValueForOption(apiWorkersOption))
    from apiDuration in ApiDuration.Create(context.ParseResult.GetValueForOption(apiDurationOption))
    select new TestConfiguration(
        eventCount,
        apiDuration,
        apiWorkers);

if (parseResult.IsFailure)
{
    consoleWriter.WriteError(parseResult.FailureError);
    context.ExitCode = 1;
    return;
}
```

Each `from` line is a step on the railway. If `EventCount.Create` fails, `WorkerCount.Create` and `ApiDuration.Create` never execute — the failure propagates directly to `parseResult`. If all three succeed, the `select` combines them into a `TestConfiguration`.

> ⚠️ **Scope restriction:** This technique is currently approved **only for input parsing at the boundary** — CLI arguments, configuration, HTTP request parameters. Do not use LINQ query syntax over `Result` for general business logic composition until the pattern has been evaluated more broadly. The restriction exists because LINQ over `Result` introduces hidden control flow (short-circuiting is invisible in the syntax), the pattern is unfamiliar to most C# developers, and debugging a failing `from` chain is harder than stepping through explicit checks. At the parsing boundary, these trade-offs are acceptable because the pattern is compact and the alternatives (nested `if` checks) are worse. In general business logic, explicit `Result` handling is clearer.

### Unwrapping — Only at the Boundary

Accessing `.Value` to extract the underlying primitive should only happen at the **boundary** — where data leaves the domain (serialization, database writes, logging, string formatting). Within the domain, code should work exclusively with the Value Object types.

```csharp
// ✅ Unwrap at the boundary — sending data to the outside world
var connectionString = $"Host={host.Value};Port={port.Value};Database={db.Value}";
logger.LogInformation("Connecting to port {Port}", port.Value);

// ✅ Domain code works with Value Objects — no unwrapping
public static Route FindRoute(HostUrl host, Port port) => ...

// ❌ Unwrapping inside domain logic — defeats the purpose
public static Route FindRoute(HostUrl host, Port port)
{
    var portNumber = port.Value;  // why? now it's just an int again
    if (portNumber > 1024) { ... }
}
```

### Operators — Avoid Unwrapping for Arithmetic and Comparison

Define operators on Value Objects so that domain code never needs to unwrap for arithmetic or comparison. The result of an operation on Value Objects should itself be a Value Object (or a `Result` if the operation can fail).

```csharp
public readonly record struct Money
{
    public decimal Value { get; }
    private Money(decimal value) => Value = value;

    public static Money operator +(Money a, Money b) => new(a.Value + b.Value);
    public static Money operator -(Money a, Money b) => new(a.Value - b.Value);
    public static Money operator *(Money a, int quantity) => new(a.Value * quantity);
    public static bool operator >(Money a, Money b) => a.Value > b.Value;
    public static bool operator <(Money a, Money b) => a.Value < b.Value;
}

// ✅ Domain code reads naturally — no .Value anywhere
var total = lineItems.Aggregate(Money.Zero, (sum, item) => sum + item.Price * item.Quantity);
if (total > customer.CreditLimit) { ... }

// ❌ Unwrapping to do math — loses type safety
var total = lineItems.Sum(item => item.Price.Value * item.Quantity);
```

### ⚠️ Arithmetic Must Not Silently Alter Values

> **This is critical.** When an arithmetic operation on Value Objects would produce an invalid result, it must **fail explicitly** — never silently clamp, truncate, or adjust the value. Silent correction is hidden behavior, which is the exact opposite of what FP aims for.

```csharp
// ✅ Default: explicit failure — the caller decides what to do
public static Result<NonNegativeInt, string> Subtract(NonNegativeInt a, NonNegativeInt b) =>
    a.Value >= b.Value
        ? NonNegativeInt.From(a.Value - b.Value)
        : Result.Fail<NonNegativeInt, string>(
            $"Subtraction would produce negative value: {a.Value} - {b.Value}");

// ✅ Domain-specific: clamping is intentional and NAMED
// Use ONLY when clamping is a real business rule (e.g., "stock cannot go below zero")
public static NonNegativeInt SubtractClamped(NonNegativeInt a, NonNegativeInt b) =>
    NonNegativeInt.From(Math.Max(0, a.Value - b.Value));
```

**The rule:** if the name doesn't communicate the behavior, the caller will be surprised. `Subtract` that silently returns 0 when `5 - 7` is requested is a bug — the caller thinks the result is meaningful when it's been quietly adjusted. `SubtractClamped` makes the intent explicit: you chose clamping, you know what you're getting.

This applies to all Value Object operations: rounding, truncation, overflow, saturation — any time the output differs from the mathematically expected result, make it explicit in the name or return a `Result`.

---

## `record struct` vs `record class`

Default to `record struct`. Switch to `record class` only when one of these applies (rule of thumb based on copying cost and mutability needs — not a hard boundary):

| Question                                     | Answer | Use             |
| -------------------------------------------- | ------ | --------------- |
| Needs inheritance? (DU variants)             | Yes    | `record class`  |
| Is it a mutable context?                     | Yes    | `record class`  |
| More than ~4 fields or contains collections? | Yes    | `record class`  |
| Otherwise                                    | —      | `record struct` |

This maps cleanly to existing patterns in the codebase: Vogen Value Objects are already `partial struct`, DU variants are forced into `record class` by C# inheritance rules, mutable contexts are `record class` by design, and most data records are small enough to be structs.

---

## Full Example — Health Monitor Module

This example pulls together the patterns from sections 4–12 in a realistic scenario: a long-running health monitor that periodically checks an endpoint, tracks degraded/unhealthy states with configurable thresholds, sends alerts on state changes, and supports graceful shutdown. It builds on the simple 3-state health checker from sections 10–11 with a richer state machine that handles intermittent failures (Degraded), recovery tracking (Recovering), and configurable check intervals per state. It demonstrates Value Objects, state machines, the context pattern, delegate-based DI, BuildDependencies, infrastructure vs. module-level delegates, the thin boundary adapter, and project structure — all in one cohesive module. `Result` appears at the consumer boundary — `CheckHealthDelegate` returns `Result<Unit, string>` — but the state machine converts it to events internally (`CheckSucceeded`, `CheckFailed`), so `Result` does not propagate through the module. `Option` does not appear. This illustrates the relationship between the two error-handling approaches: `Result` at the boundary (see section 12), state machines for ongoing processes where failure is just another transition.

### Project structure (section 8)

This is a library project consumed by an application host via `services.AddHealthMonitor(...)`. The consumer never sees the module, the context, or the transition function.

```
HealthMonitor/
├── Api.cs                              // Public surface: config, delegates, Value Objects
├── ServiceCollectionExtensions.cs      // AddHealthMonitor(...) — wires everything
└── Internal/
    ├── HealthMonitorModule.cs          // State machine, pure functions, BuildDependencies, impure functions
    ├── HealthMonitorContext.cs
    └── HealthMonitorService.cs         // BackgroundService boundary adapter
```

### State machine (section 10)

Five states, three events, every combination listed. The transition function takes config because thresholds drive the Degraded → Unhealthy and Recovering → Healthy transitions.

```csharp
public static class HealthMonitorModule
{
    // --- State machine (section 10) ---

    [Union]
    partial record HealthState
    {
        public partial record Healthy;
        public partial record Degraded(ConsecutiveCount FailureCount, string LastReason);
        public partial record Unhealthy(string Reason);
        public partial record Recovering(ConsecutiveCount SuccessCount);
        public partial record Stopped; // terminal — clean shutdown
    }

    [Union]
    partial record HealthEvent
    {
        public partial record CheckSucceeded;
        public partial record CheckFailed(string Reason);
        public partial record StopRequested;
    }

    // HealthMonitorConfig and ConsecutiveCount are declared in Api.cs (see below)

    public static HealthState Transition(HealthState current, HealthEvent @event, HealthMonitorConfig config)
        => (current, @event) switch
        {
            (HealthState.Healthy, HealthEvent.CheckSucceeded)
                => current,
            (HealthState.Healthy, HealthEvent.CheckFailed e)
                => new HealthState.Degraded(ConsecutiveCount.From(1), e.Reason),
            (HealthState.Healthy, HealthEvent.StopRequested)
                => new HealthState.Stopped(),

            (HealthState.Degraded, HealthEvent.CheckSucceeded)
                => new HealthState.Healthy(), // single success resets
            (HealthState.Degraded s, HealthEvent.CheckFailed e)
                => s.FailureCount >= config.DegradedThreshold
                    ? new HealthState.Unhealthy(e.Reason)
                    : new HealthState.Degraded(s.FailureCount + 1, e.Reason),
            (HealthState.Degraded, HealthEvent.StopRequested)
                => new HealthState.Stopped(),

            (HealthState.Unhealthy, HealthEvent.CheckSucceeded)
                => new HealthState.Recovering(ConsecutiveCount.From(1)),
            (HealthState.Unhealthy, HealthEvent.CheckFailed e)
                => new HealthState.Unhealthy(e.Reason), // update reason, stay unhealthy
            (HealthState.Unhealthy, HealthEvent.StopRequested)
                => new HealthState.Stopped(),

            (HealthState.Recovering s, HealthEvent.CheckSucceeded)
                => s.SuccessCount >= config.RecoveryThreshold
                    ? new HealthState.Healthy()
                    : new HealthState.Recovering(s.SuccessCount + 1),
            (HealthState.Recovering, HealthEvent.CheckFailed e)
                => new HealthState.Unhealthy(e.Reason), // relapse — back to unhealthy
            (HealthState.Recovering, HealthEvent.StopRequested)
                => new HealthState.Stopped(),

            (HealthState.Stopped, _)
                => current, // terminal — no transitions out
        };
}
```

### Pure functions — decisions without I/O

```csharp
public static class HealthMonitorModule
{
    // ...

    // Pure: state → check interval. No I/O, no dependencies.
    // Degraded checks more frequently to detect recovery or confirm failure faster.
    public static TimeSpan CheckInterval(HealthState state, HealthMonitorConfig config) => state.Match(
        Healthy: _ => config.NormalInterval,
        Degraded: _ => config.DegradedInterval,
        Unhealthy: _ => config.UnhealthyInterval,
        Recovering: _ => config.UnhealthyInterval,
        Stopped: _ => TimeSpan.Zero // not reached — boundary exits on Stopped
    );
}
```

### Delegates and BuildDependencies (sections 4–6)

Two layers of delegates: **infrastructure delegates** (`CheckHealthDelegate`, `SendAlertDelegate`) are declared in `Api.cs` and passed by the consumer via `AddHealthMonitor`; **module-level delegates** are internal and have startup values (logger, endpoint) baked in while runtime values (`CancellationToken`) remain as parameters — the boundary's `stoppingToken` flows through as a single authoritative cancellation source.

```csharp
public static class HealthMonitorModule
{
    // ...

    // --- Infrastructure delegates: declared in Api.cs, passed via AddHealthMonitor ---
    // (CheckHealthDelegate, SendAlertDelegate — see Api.cs below)

    // --- Internal delegates: not exposed to consumers ---
    internal delegate void LogDelegate(string message);

    // --- Module-level delegates: what the boundary sees (startup values already baked in) ---

    internal delegate Task<HealthEvent> InternalCheckDelegate(CancellationToken ct);
    internal delegate Task InternalAlertDelegate(string message);

    // --- Dependencies: the record the boundary receives ---

    internal sealed record HealthMonitorDeps(
        InternalCheckDelegate Check,
        InternalAlertDelegate Alert,
        LogDelegate Log);

    // --- BuildDependencies: bakes startup values into the module-level delegates ---

    public static HealthMonitorDeps BuildDependencies(
        IServiceProvider sp,
        HealthMonitorConfig config,
        CheckHealthDelegate checkHealth,
        SendAlertDelegate sendAlert)
    {
        var logger = sp.GetRequiredService<ILogger<HealthMonitorService>>();
        LogDelegate log = msg => logger.LogInformation("{Message}", msg);

        return new HealthMonitorDeps(
            Check: ct => CheckAsync(config.Endpoint, ct, checkHealth, log),
            Alert: msg => AlertAsync(msg, sendAlert, log),
            Log: log);
    }

    // --- Private implementations: full parameter lists including startup values ---

    private static async Task<HealthEvent> CheckAsync(
        Uri endpoint,
        CancellationToken ct,
        CheckHealthDelegate checkHealth,
        LogDelegate log)
    {
        try
        {
            var result = await checkHealth(endpoint, ct);
            return result switch
            {
                Result<Unit, string>.Success => new HealthEvent.CheckSucceeded(),
                Result<Unit, string>.Failure f => new HealthEvent.CheckFailed(f.Error),
                _ => throw new InvalidOperationException("Unreachable")
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            log($"Health check error: {ex.Message}");
            return new HealthEvent.CheckFailed(ex.Message);
        }
    }

    private static async Task AlertAsync(
        string message,
        SendAlertDelegate sendAlert,
        LogDelegate log)
    {
        try
        {
            await sendAlert(message);
            log($"Alert sent: {message}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            // Alert failure is logged but does not affect state —
            // the health monitor continues regardless of alerting infrastructure.
            log($"Alert delivery failed: {ex.Message}");
        }
    }
}
```

### Context and boundary service (section 11)

The context holds mutable state. No disposable resources — just the current health state. The boundary drives the loop:

```csharp
public record HealthMonitorContext
{
    public HealthState State { get; set; } = new HealthState.Healthy();
}
```

```csharp
public sealed class HealthMonitorService(
    HealthMonitorModule.HealthMonitorDeps deps,
    HealthMonitorConfig config) : BackgroundService
{
    private readonly HealthMonitorContext _context = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // 1) Impure: check health (endpoint baked into delegate)
            var @event = await deps.Check(stoppingToken);

            // 2) Pure: apply transition (only place state changes)
            var previous = _context.State;
            _context.State = HealthMonitorModule.Transition(
                _context.State, @event, config);

            // 3) Impure: alert on state changes
            if (_context.State is HealthState.Unhealthy u
                && previous is not HealthState.Unhealthy)
                await deps.Alert($"Endpoint unhealthy: {u.Reason}");

            if (_context.State is HealthState.Healthy
                && previous is HealthState.Recovering)
                await deps.Alert("Endpoint recovered.");

            // 4) Terminal handling
            if (_context.State is HealthState.Stopped)
            {
                deps.Log("Health monitor stopped.");
                return;
            }

            // 5) Wait — interval varies by state (pure function)
            await Task.Delay(
                HealthMonitorModule.CheckInterval(_context.State, config),
                stoppingToken);
        }
    }
}
```

### Api.cs — public surface (section 8)

The consumer of this library sees only what's in `Api.cs`. Everything else is `internal`.

```csharp
namespace HealthMonitor;

// --- Value Object: forces consumers to provide a valid value ---

[ValueObject<int>]
public partial struct ConsecutiveCount
{
    private static Validation Validate(int value) =>
        ValueObjectToolBox.ValidatePositiveInt(value);

    public static ConsecutiveCount operator +(ConsecutiveCount left, int right) =>
        From(left.Value + right);

    // Vogen generates IComparable but not relational operators by default.
    public static bool operator >=(ConsecutiveCount left, ConsecutiveCount right) =>
        left.Value >= right.Value;
    public static bool operator <=(ConsecutiveCount left, ConsecutiveCount right) =>
        left.Value <= right.Value;
    public static bool operator >(ConsecutiveCount left, ConsecutiveCount right) =>
        left.Value > right.Value;
    public static bool operator <(ConsecutiveCount left, ConsecutiveCount right) =>
        left.Value < right.Value;
}

// --- Types the consumer needs to know about ---

public sealed record HealthMonitorConfig(
    Uri Endpoint,
    ConsecutiveCount DegradedThreshold,   // consecutive failures before going Unhealthy
    ConsecutiveCount RecoveryThreshold,   // consecutive successes before returning to Healthy
    TimeSpan NormalInterval,              // check interval when Healthy
    TimeSpan DegradedInterval,            // check interval when Degraded (more frequent)
    TimeSpan UnhealthyInterval);          // check interval when Unhealthy or Recovering

// --- The consumer passes these delegates to AddHealthMonitor ---

// Result<Unit, string> from the Functional project: Success(Unit.Value) = healthy,
// Failure("reason") = unhealthy. The state machine converts these to events internally.
public delegate Task<Result<Unit, string>> CheckHealthDelegate(Uri endpoint, CancellationToken ct);
public delegate Task SendAlertDelegate(string message);
```

### ServiceCollectionExtensions.cs — wiring (section 8)

The consumer calls `services.AddHealthMonitor(config, checkHealth, sendAlert)`. The extension method wires the hosted service and calls `BuildDependencies` internally — the consumer never sees the module.

```csharp
namespace HealthMonitor;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHealthMonitor(
        this IServiceCollection services,
        HealthMonitorConfig config,
        CheckHealthDelegate checkHealth,
        SendAlertDelegate sendAlert)
    {
        services.AddSingleton(config);
        services.AddHostedService(sp =>
        {
            var deps = HealthMonitorModule.BuildDependencies(sp, config, checkHealth, sendAlert);
            return new HealthMonitorService(deps, config);
        });

        return services;
    }
}
```

### Consumer usage

Everything the consumer needs fits in a single call. The library manages health state, check intervals, alerting, and shutdown; the consumer provides the check logic and the alerting channel.

```csharp
// Program.cs in the consuming application
var config = new HealthMonitorConfig(
    Endpoint: new Uri("https://api.example.com/health"),
    DegradedThreshold: ConsecutiveCount.From(3),
    RecoveryThreshold: ConsecutiveCount.From(5),
    NormalInterval: TimeSpan.FromSeconds(30),
    DegradedInterval: TimeSpan.FromSeconds(10),
    UnhealthyInterval: TimeSpan.FromSeconds(15));

builder.Services.AddHealthMonitor(
    config,
    checkHealth: async (endpoint, ct) =>
    {
        using var client = new HttpClient();
        var response = await client.GetAsync(endpoint, ct);
        return response.IsSuccessStatusCode
            ? new Result<Unit, string>.Success(Unit.Value)
            : new Result<Unit, string>.Failure($"HTTP {(int)response.StatusCode}");
    },
    sendAlert: async message =>
    {
        // Route to Slack, PagerDuty, email, etc.
        await slackClient.PostMessageAsync("#alerts", message);
    });
```

---

## Summary

| Concept                  | Pattern                                                                                                                  |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------ |
| .NET interaction         | Thin boundary adapter — implement interfaces, inherit framework classes, catch exceptions                                |
| Constructor parameters   | Non-nullable, no null checks — wiring errors caught by integration tests                                                 |
| Exception handling       | Module functions catch specific infrastructure exceptions → data; boundary adapters catch `Exception` as safety net only |
| Code reuse               | Composition via functions; inheritance only for framework contracts                                                      |
| Code organization        | `static class` as Module — data, behavior, delegates, dependencies                                                       |
| Dependency injection     | Named delegates with `Delegate` suffix, not interfaces or direct references                                              |
| Startup values           | Baked into delegates via `BuildDependencies` or `ServiceCollectionExtensions`                                            |
| Lifecycle management     | Capture singletons directly; for scoped services, capture `IServiceScopeFactory` and resolve per-call                    |
| Runtime values           | Remain as function parameters                                                                                            |
| Project structure        | `Api.cs` + `ServiceCollectionExtensions.cs` + `Internal/` folder                                                         |
| Shared functions         | ToolBox in the most inner common folder                                                                                  |
| State machines           | Dunet DU states + Dunet DU events + single pure `Transition` with every case listed (no top-level catch-all)             |
| State management         | `XyzContext` mutable record — owned and mutated only by a single caller                                                  |
| Type safety              | Parse, don't validate — Vogen Value Objects with `From()` / `TryFrom()`                                                  |
| Value Object library     | Vogen — source-generated, with JSON, EF Core, Dapper converters built in                                                 |
| Shared validation        | `ValueObjectToolBox` with reusable validation functions composed by Vogen's `Validate`                                   |
| Parsing from other types | Static `Parse` method on the partial struct, delegates to `TryFrom()`                                                    |
| Chaining parses (ROP)    | LINQ query syntax over `Result` — scoped to input parsing at the boundary                                                |
| Unwrapping               | `.Value` only at the boundary — serialization, DB writes, logging                                                        |
| Value Object operators   | Define `+`, `-`, `>`, `<` etc. so domain code never unwraps for math                                                     |
| Value Object arithmetic  | Must fail explicitly — never silently clamp or adjust values                                                             |
| Classes                  | `sealed` by default, `static` for modules                                                                                |

---

## What This Minimizes

These patterns aren't just conceptually appealing — they tend to produce less code and strongly reduce entire categories of bugs.

**Less error handling:**

- **`Option<T>`** strongly reduces null reference exceptions from your own code. Null handling concentrates at the boundary (deserialization, third-party libraries, EF materialization), making it easier to spot because there are fewer places to look.
- **`Result<T, TFailure>`** strongly reduces `try`/`catch` blocks. Exception handling concentrates at integration seams (serialization, parsing, infrastructure calls); domain code composes Results without catch blocks.
- **Value Objects** minimize defensive validation. `if (port < 0 || port > 65535)` appears once in the `Validate` method, never again. Every function that receives a `Port` knows it's valid.
- **Discriminated Unions** make illegal states unrepresentable, minimizing defensive checks. If a state can't exist, you don't need code to guard against it.

**Less code:**

- **State Machines** minimize the number of variables and the burden of keeping them in sync. One DU value replaces a cluster of booleans, enums, and flags that would otherwise need coordinated updates.
- **Immutability** concentrates use of the `new` **keyword** at meaningful points. Records are modified with `with` expressions (which still allocate a new instance under the hood), instead of manually reconstructing objects like `var b = new B(a.X, a.Y, ...)` — so when `new` does appear explicitly, it becomes a visual cue that something significant is happening (a boundary, a starting point, a state machine reset). Immutability also eliminates defensive copies — you never clone objects to prevent mutation, and it reduces the need for locks to protect shared data structures; synchronization for shared side effects (I/O, caches, queues) and for coordinating concurrent work is still needed, though.
- **Composition over inheritance** eliminates entire layers of code that exist purely to support inheritance mechanics — no abstract base classes, no virtual/override chains, no `base.Whatever()` calls. Those layers typically exist to support code reusability, which is an antipattern: inheritance was designed for subtype polymorphism, not code reuse. Functions and composition handle reuse with less code and no coupling.
- **Baking startup values into delegates** minimizes parameter lists at every call site. When startup dependencies are captured in the delegate, callers only pass runtime values — a function that would take 6 parameters (3 startup + 3 runtime) becomes a delegate that takes 3.
- **Explicit separation of startup vs. runtime** makes it immediately obvious which values are fixed for the lifetime of the process and which change per invocation. This distinction is invisible in traditional DI where everything is a constructor parameter.
- **Static classes with static methods** can reduce total amount of code. Interfaces disappear while their methods are replaced by **delegates**. Constructors are replaced by actual wiring logic in `BuildDependencies`, which is more meaningful than just storing dependencies in fields.
- **Pure functions** minimize test setup. No mocks, no DI containers, no database seeding — just input and expected output. They also tend to be expression-bodied (`=>`), resulting in short, concise, terse functions that are easy to read at a glance.
- **Pattern matching** condenses multiple `if`/`else` chains or verbose `switch` statements into single-expression one-liners, making branching logic denser and more scannable.

---

## Performance

Performance is not the primary driver for these patterns — they are chosen for **correctness, refactor safety, and testability**. This approach is not a performance strategy; it often trades allocations for clarity (immutable records, closures, DU variants are all heap allocations that traditional mutable OOP avoids). Any performance benefits are indirect — fewer bugs, fewer retries, simpler flows — not intrinsic to the style. For business application code (which is most code), the performance characteristics are indistinguishable from traditional OOP approaches.

**Know the potential costs:**

- **Immutable `record class` with `with` expressions** creates new heap objects on every modification. This adds GC pressure if done at high frequency in tight loops.
- **Dunet unions** are `record class` (heap allocated). This is a C# language limitation — structs can't inherit from other structs, so polymorphic DU variants must be classes.
- **`ImmutableList<T>`** is slower than `List<T>` for modifications — structural sharing helps, but it's not free.

**These costs are rarely relevant** for domain logic, state machines, and business workflows. They become relevant in tight inner loops creating thousands of allocations per second — which is not where these patterns typically live.

**If you suspect a hot path, profile it.** Don't optimize based on assumptions about JIT inlining, vtable dispatch, or allocation rates. Measure, then decide.
