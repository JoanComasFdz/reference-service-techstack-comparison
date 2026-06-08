# 01-09. No Single-Implementation Interfaces

Don't create an interface just to "abstract" a class. If there is only one implementation and no polymorphic need, the interface is ceremony — it adds indirection without value.

In a codebase that follows functional programming patterns, single-implementation interfaces are a particularly strong anti-pattern. Functions are the unit of composition, not object hierarchies. Where OOP tradition reaches for `IFooService` to enable DI and testability, functional design achieves the same through **delegates** — they are the natural seam. A `SendEmailDelegate` injected as a parameter is lighter, more explicit, and composes without ceremony. See [01-02](01-02-explicit-parameters.md) for the explicit parameters principle and [01-10](01-10-composition-over-inheritance.md) for composition over inheritance.

An interface is acceptable when it describes a **contract boundary**: multiple implementations, a framework requirement, or a seam between independently deployable components. Even then, delegates are the preferred FP alternative where practical. A 1:1 `IFooService` / `FooService` pair is never justified — it's a mirror.

## Scope disclaimer

> ⚠️ **This rule requires solution-wide analysis.** Determining whether an interface has one or multiple implementations cannot be done from a single file. An auditor must scan the entire solution to count implementations. This makes the rule unsuitable for single-file grep-style audits but viable for solution-wide passes (e.g., Roslyn analyzers, IDE inspections, or LLM audits that can search across files).

## Mechanical test

A violation exists when **all** of the following are true:

1. An interface has **exactly one implementing class** in the solution
2. The interface is **not defined in an external namespace** (i.e., it's your code, not a framework contract — see framework boundary heuristic in [01-10](01-10-composition-over-inheritance.md))
3. The interface is **not required by an external framework** for DI resolution, plugin loading, or similar extensibility points

## Common violation shapes

- **The mirror pair:** `IOrderService` with a single `OrderService : IOrderService` — identical surface, zero polymorphism
- **The DI ritual:** registering `services.AddScoped<IFoo, Foo>()` where `Foo` is the only implementation and no consumer ever needs a second
- **The test-double justification:** "but I need it for mocking" — if the only reason the interface exists is to mock a class, the real problem is a missing seam. Inject a delegate instead: it's mockable, composable, and doesn't require a mirror type

## Examples

```csharp
// ❌ Avoid — mirror interface for a single-method service
internal interface IEmailSender
{
    Task SendAsync(Email email);
}

internal sealed class SmtpEmailSender : IEmailSender
{
    public Task SendAsync(Email email) { /* ... */ }
}

// ✅ Good — delegate replaces the interface, SmtpEmailSender stays as the implementation
internal delegate Task SendEmailDelegate(Email email);

internal static class SmtpEmailSender
{
    internal static Task SendAsync(Email email) { /* ... */ }
}

// Wire up in DI — the delegate is the seam, the static method is the implementation:
// services.AddSingleton<SendEmailDelegate>(SmtpEmailSender.SendAsync);
```

```csharp
// ❌ Avoid — mirror interface for a multi-method service
internal interface IOrderService
{
    Task<Result<Order>> GetOrderAsync(OrderId id);
    Task<Result<Unit>> CancelOrderAsync(OrderId id);
}

internal sealed class OrderService : IOrderService
{
    public Task<Result<Order>> GetOrderAsync(OrderId id) { /* ... */ }
    public Task<Result<Unit>> CancelOrderAsync(OrderId id) { /* ... */ }
}

// ✅ Good — each operation becomes a delegate, consumers depend only on what they use
internal delegate Task<Result<Order>> GetOrderDelegate(OrderId id);
internal delegate Task<Result<Unit>> CancelOrderDelegate(OrderId id);

internal static class OrderService
{
    internal static Task<Result<Order>> GetOrderAsync(OrderId id) { /* ... */ }
    internal static Task<Result<Unit>> CancelOrderAsync(OrderId id) { /* ... */ }
}

// Wire up in DI:
// services.AddSingleton<GetOrderDelegate>(OrderService.GetOrderAsync);
// services.AddSingleton<CancelOrderDelegate>(OrderService.CancelOrderAsync);
```

```csharp
// ⚠️ Acceptable — multiple implementations give the interface a reason to exist
internal interface INotificationChannel
{
    Task NotifyAsync(Notification notification);
}

internal sealed class SlackNotificationChannel : INotificationChannel { /* ... */ }
internal sealed class EmailNotificationChannel : INotificationChannel { /* ... */ }
internal sealed class SmsNotificationChannel : INotificationChannel { /* ... */ }

// ✅ Preferred — delegates replace the interface, same polymorphism
internal delegate Task NotifyDelegate(Notification notification);

internal static class SlackNotifier
{
    internal static Task NotifyAsync(Notification notification) { /* ... */ }
}

internal static class EmailNotifier
{
    internal static Task NotifyAsync(Notification notification) { /* ... */ }
}

internal static class SmsNotifier
{
    internal static Task NotifyAsync(Notification notification) { /* ... */ }
}

// Wire up in DI — multiple delegates, same pattern as IEnumerable<INotificationChannel>:
// services.AddSingleton<NotifyDelegate>(SlackNotifier.NotifyAsync);
// services.AddSingleton<NotifyDelegate>(EmailNotifier.NotifyAsync);
// services.AddSingleton<NotifyDelegate>(SmsNotifier.NotifyAsync);
//
// Consumer receives: IEnumerable<NotifyDelegate>
```

```csharp
// ✅ Valid — framework requires the interface (e.g., MassTransit consumer)
internal sealed class OrderPlacedConsumer : IConsumer<OrderPlacedEvent> { /* ... */ }
```

> 🔍 **Audit signature**
>
> 1. **Mirror pair naming:** `I{Name}` interface with a single `{Name} : I{Name}` class — strongest signal, can be detected with a naming convention scan
> 2. **Single implementation count:** interface defined in solution code with `class ... : IFoo` appearing exactly once across the entire solution
> 3. **Same-project co-location:** interface and its sole implementation live in the same project/assembly — no cross-boundary justification
> 4. **DI registration as sole consumer:** `services.Add*(I{Name}, {Name})` where no other code resolves `I{Name}` for polymorphic dispatch (e.g., `IEnumerable<I{Name}>`)

> **Not a violation (but delegates are preferred where practical):**
>
> - Interface with 2+ implementations in the solution (acceptable — but consider delegates with `IEnumerable<TDelegate>` as the FP alternative)
> - Interface defined in an external namespace (framework contract — see [01-10](01-10-composition-over-inheritance.md) framework boundary heuristic)
> - Interface in a shared contracts assembly consumed by independently deployable services (even if only one implementation exists _today_ in _this_ solution)
> - Interface required for polymorphic DI resolution (e.g., `IEnumerable<IHandler>`) — but consider `IEnumerable<TDelegate>` as the FP equivalent
