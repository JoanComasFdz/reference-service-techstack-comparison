# 01-01. Static Classes for Pure Logic

## Intent

This rule moves the codebase closer to a **functional programming style**. A static class in C# is the closest equivalent to a **Haskell module** — a named container for pure functions with no hidden state.

If a class has no instance fields and no instance properties, make it `static`. This signals to developers: "these are pure functions grouped by cohesion, no instance needed."

## Rule

A class that holds **zero instance fields** and **zero instance properties** must be declared `static`. All data the functions need flows in through parameters and out through return values.

```csharp
// ✅ Good — static class acts as a function container (Haskell module style)
internal static class ServiceMetricsPlotBuilder
{
    public static Plot Build(ResourceMetricsReport? data, ChartConfig config)
    {
        var series = BuildSeries(data, config.MaxDataPoints);
        return new Plot(series, config.Title);
    }

    public static ChartConfig WithOverrides(ChartConfig config, PlotOverrides overrides) =>
        config with { Title = overrides.Title ?? config.Title };
}

// ❌ Avoid — instance class wrapping state that should be a parameter
internal sealed class ServiceMetricsPlotBuilder
{
    private readonly ChartConfig _config;

    public ServiceMetricsPlotBuilder(ChartConfig config) => _config = config;

    public Plot Build(ResourceMetricsReport? data)
    {
        var series = BuildSeries(data, _config.MaxDataPoints);
        return new Plot(series, _config.Title);
    }

    public ChartConfig WithOverrides(PlotOverrides overrides) =>
        _config with { Title = overrides.Title ?? _config.Title };
}

// ❌ Avoid — primary constructor hiding instance state
internal sealed class ServiceMetricsPlotBuilder(ChartConfig config)
{
    public Plot Build(ResourceMetricsReport? data)
    {
        var series = BuildSeries(data, config.MaxDataPoints);
        return new Plot(series, config.Title);
    }

    public ChartConfig WithOverrides(PlotOverrides overrides) =>
        config with { Title = overrides.Title ?? config.Title };
}
```

## DI Considerations

Static classes are **not registered in the DI container**. This is intentional — pure functions don't need lifetime management or substitution. Call them directly.

## Exceptions

- **Framework boundaries:** A class that implements an interface or inherits from a base class **required by an external framework** is exempt even if it would otherwise be stateless. The framework demands an instance. Heuristic: if the interface or base class is defined in a namespace outside your solution (e.g., `Microsoft.*`, `System.*`, `MassTransit.*`, or any third-party library), and removing the class would break a framework contract, it qualifies.
- **`record` types** are out of scope for this rule.
- **`abstract` classes** are out of scope. See rule **01-XX** _(abstract class rules)_.

> 🔍 **Audit signature**
>
> **Mechanical test (auto-confirm):** A non-`static`, non-`abstract`, non-`record` class is a violation if **all** of the following hold:
>
> 1. Zero explicitly declared instance fields
> 2. Zero primary constructor parameters
> 3. Zero instance auto-properties or instance properties with backing logic
> 4. Does not implement any interface or inherit from any base class defined outside the solution
>
> **Flag for review:** A non-`static`, non-`abstract`, non-`record` class that meets conditions 1 and 3 above but **has primary constructor parameters** and does **not** implement/inherit from an external framework type. These classes capture implicit instance state that may need to be refactored to parameter-passing on a static class.
>
> **Violation patterns:**
>
> 1. `internal sealed class Foo { ... }` with only static-eligible members and no instance state → must be `internal static class Foo`
> 2. `internal sealed class Foo(SomeDep dep) { ... }` where `Foo` does not implement/inherit any external framework type → flag: primary constructor captures state that should likely flow as function parameters
>
> **Not a violation:**
>
> - Class implements/inherits a type defined outside the solution (framework boundary)
> - Class is `abstract`
> - Class is a `record`
> - Class declares any instance field or instance property
