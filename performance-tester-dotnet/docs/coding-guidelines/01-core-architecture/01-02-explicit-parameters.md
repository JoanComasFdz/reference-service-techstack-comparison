# 01-02. Explicit Parameters Over Hidden State

Pass all dependencies as method parameters, not constructor injection. Makes data flow visible at the call site and moves the codebase toward a **functional, stateless style** where all inputs are explicit.

## Why

This rule enforces the separation of data and behaviour that underpins functional programming in C#. Behaviour lives in functions; data lives in records. When every dependency is a parameter, the method is one refactor away from becoming `static`, and one step closer to being classified as pure, impure, or part of an impure sandwich.

No judgment is required to audit this rule: if a method reads an instance field, it has hidden state. The fix is always the same — promote the field to a parameter.

## Scope

**Applies to:** All classes that represent behaviour (services, builders, handlers, helpers, processors, etc.).

**Does not apply to:**

- **Records and data types.** This rule governs behaviour, not data. Records, DTOs, and value objects that carry state by design are excluded.
- **Framework boundary classes.** A class that implements an interface or inherits from a base class **required by an external framework** is exempt even if it would otherwise be stateless — the framework demands an instance. Heuristic: if the interface or base class is defined in a namespace outside your solution (e.g., `Microsoft.*`, `System.*`, `MassTransit.*`, or any third-party library), and removing the class would break a framework contract, it qualifies. Examples: `ControllerBase`, `BackgroundService`, `DbContext`, `IHostedService`, `IConsumer<T>`.

## Examples

```csharp
// ✅ Good — all inputs explicit, one step from static
public static Plot Build(ResourceMetricsReport? data, ChartConfig config)

// ✅ Good — builder returns new record, no accumulated state
public static ChartConfig WithWidth(ChartConfig current, int width)
    => current with { Width = width };
```

```csharp
// ❌ Avoid — hidden dependency via field
public class ChartBuilder
{
    private readonly ChartConfig _config; // injected, never mutated

    public ChartBuilder(ChartConfig config) => _config = config;

    public Plot Build(ResourceMetricsReport? data)
    {
        var width = _config.Width; // hidden input
        // ...
    }
}

// ❌ Avoid — stateful builder accumulating state in fields
public class ChartConfigBuilder
{
    private int _width;
    private int _height;

    public ChartConfigBuilder WithWidth(int w) { _width = w; return this; }
    public ChartConfigBuilder WithHeight(int h) { _height = h; return this; }
    public ChartConfig Build() => new(_width, _height);
}

// ❌ Avoid — entity/aggregate with behaviour reading own state
public class Order
{
    private readonly List<OrderLine> _lines;
    public decimal Total() => _lines.Sum(l => l.Price * l.Quantity); // hidden state
}
```

```csharp
// ✅ Fix for the aggregate — behaviour is a static function over data
public static decimal CalculateTotal(IReadOnlyList<OrderLine> lines)
    => lines.Sum(l => l.Price * l.Quantity);
```

> 🔍 **Audit signature**
>
> **Violation patterns:**
>
> 1. A non-static method in a behaviour class reads any instance field or property — the field is a hidden parameter.
> 2. A method mutates `this` or any instance field (e.g., fluent builder accumulating state) instead of returning a new value.
> 3. A constructor stores dependencies into fields that are later read by methods — these dependencies should be method parameters.
>
> **Excluded from audit:**
>
> - `record`, `record struct`, and types attributed as data types (DTOs, value objects).
> - Classes that implement a framework-required interface or inherit a framework base class — identified by the base type/interface being defined in a namespace outside the solution (e.g., `Microsoft.*`, `System.*`, `MassTransit.*`, or any third-party library).

---

## Re-Evaluation

## Rule: 01-02 — Explicit Parameters Over Hidden State

### Auditability Grade: A

With the functional-programming framing, the rule reduces to a mechanical check: does a non-static method in a behaviour class read or write any instance member? The only classification needed — behaviour class vs. data type vs. framework boundary — is determinable from syntax (class vs. record, presence of framework base type/interface).

### Current Fitness

| Question                                    | Answer                                                                                                |
| ------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| Has ❌ Avoid example?                       | Yes — three distinct violation shapes (hidden dep, stateful builder, entity with behaviour)           |
| Violation is a mechanical test?             | Yes — "method reads instance field in a behaviour class"                                              |
| Auditor can enumerate syntactic signatures? | Yes — (1) field read in method body, (2) `this` mutation, (3) constructor-to-field-to-method pipeline |
| Boundaries clearly stated?                  | Yes — records excluded, framework boundaries excluded, scope defined by class purpose                 |

### Gaps

- **Extension methods on records** are implicitly compliant (they're static), but this could be stated for completeness.

### Recommendation: ✅ Audit-ready

The rule is now fit for automated audit. The namespace heuristic for framework boundaries makes all three exclusion checks mechanical: `record` keyword, external namespace on base type/interface, and instance field access.

### Proposed Changes

None — rule is audit-ready.

### Cross-References (Discovery)

```
Mentioned in rule text: none
Suspected overlap: 01-01 (pure/impure function classification), any rule governing static methods or impure sandwich pattern
```
