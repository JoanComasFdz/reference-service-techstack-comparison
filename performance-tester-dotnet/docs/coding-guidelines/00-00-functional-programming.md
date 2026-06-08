# 00-00. Functional Programming Approach

> ⚙️ **Role:** Foundational principle. This rule is not audited directly — it provides the "why" behind every leaf rule in the guidelines. LLMs generating code should internalize these principles to produce idiomatic output without needing a rule for every situation.

This codebase follows a **functional-first approach in C#**. Object-oriented features are used where they serve us (records, sealed hierarchies), but the default mental model is functional: data is inert, behavior is functions, and side effects are pushed to the edges.

---

## 1. Data ≠ Behavior

Data structures hold values. Functions operate on them. They don't live together in the same type.

```csharp
// ✅ Data is a record — no methods, no logic
public record Order(OrderId Id, CustomerId Customer, ImmutableList<OrderLine> Lines, Money Total);

// ✅ Behavior is a static function that takes data in and returns data out
public static class OrderOperations
{
    public static Money CalculateTotal(ImmutableList<OrderLine> lines) =>
        lines.Aggregate(Money.Zero, (sum, line) => sum + line.Price * line.Quantity);
}

// ❌ OOP bundle — data and behavior coupled in one class
public class Order
{
    public List<OrderLine> Lines { get; set; }
    public decimal Total { get; private set; }

    public void AddLine(OrderLine line)
    {
        Lines.Add(line);
        Total = Lines.Sum(l => l.Price * l.Quantity);
    }
}
```

**Why:** When data and behavior are separate, each can be tested, composed, and reasoned about independently. Data can be serialized, compared, and logged without dragging behavior along. Functions can be reused across different data shapes.

---

## 2. Immutability

Data is created, never modified. Changing something means creating a new instance.

```csharp
// ✅ Record with immutable collection — "modify" via with-expression
public record ShoppingCart(CustomerId Customer, ImmutableList<CartItem> Items);

var updated = cart with { Items = cart.Items.Add(newItem) };

// ❌ Mutable state — who changed it? when? what thread?
public class ShoppingCart
{
    public List<CartItem> Items { get; } = new();
    public void Add(CartItem item) => Items.Add(item);
}
```

**C# toolkit:** `record` / `record struct`, `init` properties, `readonly struct`, `ImmutableList<T>`, `ImmutableDictionary<K,V>`, `with` expressions, `FrozenSet<T>`.

**Why:** Immutable data eliminates temporal coupling (method call order doesn't matter), race conditions (no shared mutable state), and a whole class of bugs where something was modified unexpectedly.

---

## 3. Explicit Absence — `Option<T>`

Use `Option<T>` to represent values that might not exist. Never return `null` from domain code.

```csharp
// ✅ Return type tells the caller: this might not be found
public static Option<Customer> FindByEmail(ImmutableList<Customer> customers, Email email) =>
    customers.FirstOrDefault(c => c.Email == email) is { } found
        ? Option.Some(found)
        : Option.None<Customer>();

// Caller is forced to handle absence
var greeting = FindByEmail(customers, email)
    .Map(c => $"Hello, {c.Name}")
    .GetOr("Customer not found");

// ❌ Null — caller might forget to check, NullReferenceException at runtime
public static Customer? FindByEmail(List<Customer> customers, string email) =>
    customers.FirstOrDefault(c => c.Email == email);
```

**Why:** `Option<T>` moves "might be absent" from a runtime surprise to a compile-time contract. The type signature communicates intent; the API makes forgetting to handle absence impossible.

---

## 4. Discriminated Unions

When a value can be exactly one of a finite set of shapes, model it as a **discriminated union** — a type where each variant has its own structure, and the compiler can verify that all variants are handled.

```csharp
// A payment can be exactly one of these three shapes — nothing else
abstract record PaymentMethod
{
    record CreditCard(CardNumber Number, ExpiryDate Expiry) : PaymentMethod;
    record BankTransfer(Iban Iban) : PaymentMethod;
    record CryptoWallet(WalletAddress Address) : PaymentMethod;
}

// Handling every variant explicitly via switch expression
public static Fee CalculateFee(PaymentMethod method) => method switch
{
    PaymentMethod.CreditCard cc => cc.Number.IsAmex ? Fee.High : Fee.Standard,
    PaymentMethod.BankTransfer => Fee.Low,
    PaymentMethod.CryptoWallet => Fee.Variable,
    _ => throw new InvalidOperationException("Unknown payment method"),
};
```

**Why:** Discriminated unions make illegal states unrepresentable. If a function receives a `PaymentMethod`, it knows the value is one of exactly three shapes — not some arbitrary object. Combined with pattern matching, every handler is forced to consider each variant explicitly.

> C# doesn't have native discriminated unions. Guideline 00-01 describes how we implement them using the **Dunet** source generator, which provides compile-time exhaustiveness guarantees that go beyond what a `switch` with `_` can offer.

---

## 5. Explicit Failure — `Result<TSuccess, TFailure>`

Use `Result<TSuccess, TFailure>` for operations that can fail in expected ways. Reserve exceptions for infrastructure faults and truly unexpected conditions.

```csharp
// ✅ The return type communicates: this can fail, and here's what failure looks like
public static Result<OrderConfirmation, PlaceOrderFailure> PlaceOrder(Order order, InventorySnapshot inventory)
{
    if (order.Lines.IsEmpty)
        return new PlaceOrderFailure.EmptyOrder();

    var unavailable = order.Lines.Where(l => !inventory.IsAvailable(l.ProductId, l.Quantity));
    if (unavailable.Any())
        return new PlaceOrderFailure.InsufficientStock(unavailable.Select(l => l.ProductId).ToImmutableList());

    return new OrderConfirmation(order.Id, DateTimeOffset.UtcNow);
}

// ✅ Failure type is a discriminated union (section 4) — each failure case is explicit
abstract record PlaceOrderFailure
{
    record EmptyOrder() : PlaceOrderFailure;
    record InsufficientStock(ImmutableList<ProductId> Products) : PlaceOrderFailure;
}

// ❌ Exception for an expected business case
public static OrderConfirmation PlaceOrder(Order order)
{
    if (order.Lines.Count == 0)
        throw new InvalidOperationException("Order has no lines"); // caller may not catch this
    // ...
}
```

**Boundary between Result and exceptions:**

| Situation                                                | Mechanism                                                |
| -------------------------------------------------------- | -------------------------------------------------------- |
| Business rule violation (validation, insufficient stock) | `Result<T, TFailure>`                                    |
| Item not found in a lookup                               | `Option.None`                                            |
| Network timeout, disk failure, out of memory             | Exception                                                |
| Third-party library throws                               | Catch → convert to `Result<T, TFailure>` at the boundary |

**Why:** Exceptions are invisible in type signatures and create hidden control flow. `Result<TSuccess, TFailure>` makes the failure path explicit, composable, and impossible to accidentally ignore. The typed failure (a discriminated union) tells the caller exactly what can go wrong.

**`out` parameters are forbidden.** The `TryXxx(out T result)` pattern is idiomatic C# (e.g., `int.TryParse()`), but it is fundamentally incompatible with FP: it relies on mutation, it cannot be composed in pipelines, and it splits a function's output into two channels (return value + out parameter) instead of expressing it as a single value. `Result<T, TFailure>` does the same job and is explicit, composable, and first-class.

```csharp
// ❌ out parameter — mutation, two output channels, cannot compose
public static bool TryParsePort(string input, out Port port) { ... }

// ✅ Result — single return value, composable, explicit
public static Result<Port, string> ParsePort(string input) => ...
```

> **Note on Either:** `Result<TSuccess, TFailure>` is a specialized form of `Either<Left, Right>` where Left = Failure and Right = Success. In this codebase, prefer `Result<TSuccess, TFailure>` for error handling and discriminated unions for general "one of N types" modeling. Raw Either is rarely needed.

---

## 6. Railway-Oriented Programming

When multiple operations each return `Result<T, TFailure>`, they can be **chained** so that if any step fails, the remaining steps are skipped and the first failure propagates through. This is known as railway-oriented programming — the computation runs on two tracks: the success track and the failure track. A failure at any point switches to the failure track for the rest of the pipeline.

```
[Input] → [Parse A] → [Parse B] → [Parse C] → [Combine] → Success
               |             |            |
               └─────────────┴────────────┴──→ Failure (first error)
```

**Why:** Without composition, chaining fallible operations leads to deeply nested `if (result.IsSuccess)` checks or early returns at every step. Railway-oriented programming flattens this into a linear pipeline where the error handling is implicit in the structure — each step only runs if all previous steps succeeded.

This concept originates from Scott Wlaschin's [_Railway Oriented Programming_](https://fsharpforfunandprofit.com/rop/) in F#, which is itself an application of monadic bind (`>>=`) from Haskell. The idea is the same across all functional languages: chain operations that can fail, short-circuit on first failure, and collect the result at the end.

> Guideline 00-01 describes the specific technique we use in C# to achieve this: LINQ query syntax over `Result<T, TFailure>`, currently scoped to **input parsing at the boundary**.

---

## 7. Pattern Matching

Handle every possible case explicitly using C# switch expressions.

When the set of possible values is finite and closed (a sealed hierarchy or an enum you own), list every case explicitly — no discard. If the set is not fully enumerable (e.g., `string`, `int`, or an enum where new values might be added later), you **must** use the discard pattern (`_`) to handle any unmatched input.

```csharp
// ✅ Closed enum you own — list every case, no discard
public static string Describe(OrderStatus status) => status switch
{
    OrderStatus.Pending => "Waiting for payment",
    OrderStatus.Confirmed => "Payment received",
    OrderStatus.Shipped => "On the way",
    OrderStatus.Delivered => "Arrived",
    OrderStatus.Cancelled => "Cancelled",
};

// ✅ Discriminated union — list every variant
public static Fee CalculateFee(PaymentMethod method) => method switch
{
    PaymentMethod.CreditCard cc => cc.Number.IsAmex ? Fee.High : Fee.Standard,
    PaymentMethod.BankTransfer => Fee.Low,
    PaymentMethod.CryptoWallet => Fee.Variable,
    _ => throw new InvalidOperationException("Unknown payment method"),
};

// ✅ Open type (string) — discard required to handle unknown values
public static LogLevel ParseLevel(string level) => level.ToLowerInvariant() switch
{
    "debug" => LogLevel.Debug,
    "info" => LogLevel.Information,
    "warn" => LogLevel.Warning,
    "error" => LogLevel.Error,
    _ => LogLevel.Information, // unknown strings default safely
};
```

**Why:** Exhaustive pattern matching turns "did I handle all cases?" from a code review question into a compiler guarantee. When you list all cases explicitly, adding a new variant or enum value produces a compiler warning at every `switch` that doesn't handle it.

> Guideline 00-01 describes how **Dunet** provides an even stronger guarantee: a generated `Match()` method whose parameter list **is** the set of variants — making exhaustiveness a compile **error**, not just a warning.

---

## 8. Pure Functions and Side Effects

A **pure function** depends only on its parameters and returns a value without touching the outside world. Same input → same output, always.

A **side effect** is any interaction with the world outside the function: database, file system, network, clock, random, logging, mutable shared state.

```csharp
// ✅ Pure — only reads parameters, returns a new value
public static Money ApplyDiscount(Money price, Percentage discount) =>
    price * (1 - discount.Value);

// ✅ Pure — transforms immutable data, no I/O
public static ImmutableList<OrderLine> RemoveCancelledLines(
    ImmutableList<OrderLine> lines,
    ImmutableHashSet<ProductId> cancelledProducts) =>
    lines.Where(l => !cancelledProducts.Contains(l.ProductId)).ToImmutableList();

// ❌ Impure — reads clock (non-deterministic), writes to database (side effect)
public static void ApplyDiscount(Order order, decimal discount)
{
    order.Total *= (1 - discount);
    order.ModifiedAt = DateTime.Now;
    _repository.Save(order);
}
```

**Recognizing side effects — a function is impure if it:**

- Reads or writes a database, file, or network
- Reads the clock or generates random values
- Mutates shared or static state
- Mutates its input parameters
- Logs
- Throws exceptions
- Publishes or consumes events/messages
- Starts or manipulates threads

**Why:** Pure functions are trivially testable (no mocks needed), trivially parallelizable (no shared state), and trivially reusable (no hidden dependencies). The more logic lives in pure functions, the more of your codebase has these properties.

---

## 9. First-Class Functions

Functions can be assigned to variables, passed as arguments, and returned from other functions. In C#, this is expressed through delegates (`Func<T>`, `Action<T>`, and named delegates).

```csharp
// A function stored in a variable
Func<Money, Percentage, Money> applyDiscount = (price, discount) => price * (1 - discount.Value);

// A function passed as a parameter (named delegate — see Guideline 02-01)
public delegate Money CalculateShippingCost(Address destination, Weight totalWeight);

public static OrderTotal CalculateTotal(Order order, CalculateShippingCost calculateShipping) =>
    new(order.Subtotal, calculateShipping(order.Address, order.TotalWeight));
```

**Why:** Treating functions as values is what makes the rest of the architecture possible. Without first-class functions, passing behavior into pure code requires interfaces and ceremony. With them, a `delegate` parameter is all you need.

---

## 10. Higher-Order Functions

Functions that take other functions as parameters or return functions. This is how pure business logic accepts impure capabilities without depending on them directly.

```csharp
// Takes a function parameter — this is a higher-order function
public static async Task<ImmutableList<Result<T, TFailure>>> ProcessBatch<T, TFailure>(
    ImmutableList<T> items,
    Func<T, Task<Result<T, TFailure>>> processItem) =>
    (await Task.WhenAll(items.Select(processItem))).ToImmutableList();

// Returns a function — factory pattern, functional style
public static Func<Order, Money> CreateDiscountCalculator(CustomerTier tier) => tier switch
{
    CustomerTier.Gold => order => order.Subtotal * 0.15m,
    CustomerTier.Silver => order => order.Subtotal * 0.10m,
    _ => order => Money.Zero,
};
```

**Why:** Higher-order functions are the bridge between the pure core and the impure shell. The pure core declares _what behavior it needs_ as a function parameter; the impure shell supplies the concrete implementation at the call site.

---

## 11. Pure Core, Impure Shell (Functional Architecture)

Also known as the **Impure–Pure–Impure Sandwich** or **Impureheim**.

Structure code as a sandwich: impure edges that gather data and perform effects, with a pure core that contains all business logic and decisions.

```
Impure: Read from database, API, file system, clock
  ↓ immutable data
Pure:  Validate, transform, decide, calculate
  ↓ immutable result describing what to do
Impure: Write to database, send events, respond to caller
```

```csharp
// The handler is the thin impure shell — it orchestrates I/O
public static async Task Handle(PlaceOrderCommand command)
{
    // --- Impure: gather data ---
    var order = await orderRepository.GetById(command.OrderId);
    var inventory = await inventoryService.GetSnapshot();

    // --- Pure: all business logic ---
    Result<OrderConfirmation, PlaceOrderFailure> result = OrderOperations.PlaceOrder(order, inventory);

    // --- Impure: act on the decision ---
    result.Switch(
        confirmation => await orderRepository.Save(confirmation),
        failure => logger.LogWarning("Order rejected: {Failure}", failure));
}

// OrderOperations.PlaceOrder is pure — no async, no I/O, no injected dependencies
// It takes immutable data in, returns an immutable result out
```

**Why:**

- **Testing without mocks.** The pure core is tested by passing immutable data in and asserting on the immutable result out. No mocks, no stubs, no test doubles, no DI container in tests. If your business logic needs mocks to be tested, it's not pure yet — push the I/O outward until it doesn't.
- **Predictability.** Pure functions are deterministic — same input, same output, every time. No "it works on my machine" caused by database state, clock skew, or network timing.
- **Maximized pure surface area.** The more logic lives in the pure core, the more of your codebase gets all of the above properties for free. The impure shell should be so thin and boring that it barely needs testing.
- **Clear failure boundaries.** Side effects are isolated to the edges, making it obvious where things can go wrong and where to look when they do.

---

## How This Connects

Every specific guideline in this codebase traces back to these principles. **Guideline 00-01** describes how to apply them concretely in C# and .NET. The individual leaf rules (02-xx, 03-xx, etc.) are mechanical extractions of specific patterns from 00-01.

When you encounter a situation not covered by a specific leaf rule, apply these principles directly. They are the source; the leaf rules are the consequences.
