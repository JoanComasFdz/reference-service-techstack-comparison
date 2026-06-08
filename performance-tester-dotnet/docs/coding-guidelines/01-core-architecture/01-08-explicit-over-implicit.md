# 01-08. Explicit Over Implicit

> **Scope:** Design principle for human developers and PR reviewers — not subject to automated audit because "too much indirection" requires reasoning about reader comprehension and codebase familiarity, which is inherently a judgment call.

Prefer visible code paths over convention-driven magic. A developer reading a call site, a registration, or a pipeline should be able to understand what happens without consulting framework documentation, runtime behavior, or hidden conventions.

This is not about avoiding abstraction — good abstractions _clarify_. The target is **invisible behavior**: things that happen because of naming conventions, reflection, attributes that trigger non-obvious side effects, or frameworks that "just work" until they don't.

## What "Implicit" Looks Like

### Convention-based DI registration (assembly scanning)

```csharp
// ❌ Avoid — which types get registered? With what lifetime?
// Reader must know the convention to understand the behavior.
services.AddAllTypesFrom(typeof(Startup).Assembly);

// ✅ Prefer — every registration is visible and searchable
services.AddSingleton<IOrderValidator, OrderValidator>();
services.AddScoped<IPaymentGateway, StripePaymentGateway>();
services.AddScoped<IOrderRepository, SqlOrderRepository>();
```

### Reflection-based or attribute-driven mapping

```csharp
// ❌ Avoid — what maps to what? Silently ignores mismatches at runtime.
var dto = _mapper.Map<OrderDto>(order);

// ✅ Prefer — mapping is visible, compiler-checked, and greppable
var dto = new OrderDto(
    Id: order.Id,
    CustomerName: order.Customer.Name,
    Total: order.Lines.Sum(l => l.Price * l.Quantity),
    Status: order.Status.ToDisplayString());
```

### Middleware or behavior pipelines wired by convention

```csharp
// ❌ Avoid — validation happens because of a naming convention
// (ValidationBehavior<T> matches any request that has a matching validator class).
// Reader at the call site sees none of this.
services.AddMediatR(cfg => {
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
});

// ✅ Prefer — pipeline is explicit at the composition root
app.MapPost("/orders", async (CreateOrderRequest req) =>
{
    var validated = OrderValidator.Validate(req);   // Visible
    using var tx = await db.BeginTransaction();      // Visible
    var result = await CreateOrder.Execute(validated, db);
    await tx.CommitAsync();
    return result.ToResponse();
});
```

### Attributes with non-obvious side effects

```csharp
// ❌ Avoid — attribute triggers retry + circuit breaker at runtime,
// but the reader sees a normal method signature
[Resilient(retries: 3, circuitBreaker: true)]
public async Task<Order> GetOrder(OrderId id) { ... }

// ✅ Prefer — resilience is visible in the call chain
public async Task<Order> GetOrder(OrderId id) =>
    await RetryPolicy.ExecuteAsync(
        () => _httpClient.GetOrder(id),
        maxRetries: 3);
```

### String-based or key-based configuration lookup

```csharp
// ❌ Avoid — typo in the key = silent null at runtime
var timeout = config["Services:Payment:TimeoutMs"];

// ✅ Prefer — strongly typed, compiler-checked, discoverable
var timeout = config.Payment.TimeoutMs;  // via IOptions<PaymentConfig>
```

## Explicit in Functional Style

Functional programming _is_ explicit programming — when done well. The patterns below make data flow, error paths, and absence visible in the type system rather than hiding them behind exceptions, nulls, or side effects.

### Errors as values, not exceptions

```csharp
// ❌ Avoid — caller has no idea this can fail, or how
public Order CreateOrder(CreateOrderRequest request)
{
    var customer = _db.GetCustomer(request.CustomerId);  // throws if not found?
    if (customer.Balance < request.Total)
        throw new InsufficientFundsException();           // invisible to caller
    // ...
}

// ✅ Prefer — the return type tells the full story
public Result<Order, CreateOrderError> CreateOrder(CreateOrderRequest request) =>
    _db.FindCustomer(request.CustomerId)
        .ToResult(CreateOrderError.CustomerNotFound)
        .Ensure(c => c.Balance >= request.Total, CreateOrderError.InsufficientFunds)
        .Map(customer => BuildOrder(customer, request));
// The caller MUST handle the error — the compiler enforces it.
```

### Absence as Option, not null

```csharp
// ❌ Avoid — null is invisible; nothing forces the caller to check
public Customer? FindCustomer(CustomerId id) =>
    _customers.FirstOrDefault(c => c.Id == id);
// Caller may cheerfully dereference without checking.

// ✅ Prefer — Option<T> makes absence explicit in the type
public Option<Customer> FindCustomer(CustomerId id) =>
    _customers
        .FirstOrDefault(c => c.Id == id)
        .ToOption();
// Caller must .Match(), .Map(), or .DefaultValue() — absence is unignorable.
```

### Pure functions over hidden state mutation

```csharp
// ❌ Avoid — what changed? Reader must inspect _state to know.
public void ApplyDiscount(Order order)
{
    _discountTracker.MarkUsed(order.CustomerId);  // side effect buried inside
    order.Total *= 0.9m;                           // mutates the argument
    _metrics.Increment("discounts_applied");       // another hidden side effect
}

// ✅ Prefer — inputs in, output out, nothing else happens
public DiscountedOrder ApplyDiscount(Order order, CalculateDiscountDelegate calculateDiscount) =>
    new DiscountedOrder(
        Order: order with { Total = order.Total - calculateDiscount(order) },
        DiscountApplied: calculateDiscount(order));
// Side effects (tracking, metrics) happen at the composition root, visibly.
```

### Explicit data transformation pipelines

```csharp
// ❌ Avoid — each step mutates shared state; order of calls matters
//           but nothing in the code makes that dependency visible
processor.LoadRawData(file);
processor.NormalizeTimestamps();
processor.FilterOutliers();
processor.Aggregate();
var result = processor.GetResult();

// ✅ Prefer — data flows through the pipeline; each step is a function
var result = LoadRawData(file)
    .Pipe(NormalizeTimestamps)
    .Pipe(FilterOutliers)
    .Pipe(AggregateByHour);
// No hidden state. Reorder = compiler error or obviously wrong types.
```

### Explicit dependencies over ambient context

```csharp
// ❌ Avoid — function secretly reads from ambient/static state
public decimal CalculateShipping(Order order) =>
    ShippingRateCache.Instance                    // singleton hiding a dependency
        .GetRate(RegionContext.Current, order.Weight);  // where does Current come from?

// ✅ Prefer — every input is a parameter
public decimal CalculateShipping(Order order, Region region, ShippingRates rates) =>
    rates.GetRate(region, order.Weight);
// Testable, readable, no surprises.
```

## When Indirection Is Acceptable

Not every abstraction is "implicit." These are fine:

- **Strongly-typed Options/configuration** bound at startup — the binding is explicit even if the values come from appsettings.
- **Extension methods** that encapsulate common patterns — the call site reads clearly and the implementation is one `Go to Definition` away.
- **Higher-order functions** like `.Select()`, `.Where()`, `.Pipe()` — the behavior is the lambda or function you pass in, not hidden convention.
- **Dependency injection itself** — constructor parameters declare dependencies explicitly; the wiring is visible in the composition root.
- **Result/Option chaining** (`.Map()`, `.Bind()`, `.Match()`) — these are explicit _because_ the types force the caller to handle every case. The abstraction makes hidden paths _impossible_, not invisible.
- **LINQ pipelines** — each transformation step is a visible function; the data flow reads top to bottom.
- **`record` / `with` expressions** — creating new values from old ones is explicit immutable transformation, not hidden mutation.

## The Litmus Test

Ask: _"If I'm reading this call site for the first time, can I understand what happens without knowing a framework convention or checking runtime behavior?"_

If the answer is no, the code is too implicit.
