# 01-10. Composition Over Inheritance

Data and behaviour are separate concerns. Don't use base classes to share behaviour — compose functions instead.

Inheritance is valid for **data hierarchies** (discriminated unions, record inheritance) and **framework requirements** (`ControllerBase`, `BackgroundService`, etc.). It is not valid for sharing executable logic between behaviour classes.

## Mechanical test

A violation exists when a **behaviour class** (a class containing methods with logic, not a data type — see [01-02](01-02-explicit-parameters.md) for the classification heuristic) inherits from a **non-framework base class** AND does any of:

1. **Calls a `base.Method()`** — reusing parent behaviour
2. **Overrides a `virtual` or `abstract` method** — participating in a template method pattern
3. **Reads or writes a `protected` field/property** from the base — sharing state through inheritance

> **Not a violation:**
>
> - **Framework boundaries:** inheriting from a base class required by an external framework. Heuristic: if the base class is defined in a namespace outside your solution (e.g., `Microsoft.*`, `System.*`, `MassTransit.*`, or any third-party library), and removing the inheritance would break a framework contract, it is exempt.
> - Record/class inheritance for data modelling (discriminated unions, DTOs)
> - Implementing interfaces (composition contracts, not inheritance)

## Examples

```csharp
// ❌ Avoid — sharing behaviour through a base class
internal abstract class MetricsPlotBuilderBase
{
    protected PlotModel CreateBasePlot(string title) { /* shared logic */ }
    protected abstract void AddSeries(PlotModel plot);

    public PlotModel Build(MetricsReport data)
    {
        var plot = CreateBasePlot(data.Title);   // shared logic in base
        AddSeries(plot);                          // template method
        return plot;
    }
}

internal sealed class ServiceMetricsPlotBuilder : MetricsPlotBuilderBase
{
    protected override void AddSeries(PlotModel plot) { /* ... */ }
}

// ✅ Good — compose via delegate, dependency is explicit
internal delegate PlotModel CreateBasePlotDelegate(string title);

internal static class ServiceMetricsPlotBuilder
{
    internal static PlotModel Build(MetricsReport data, CreateBasePlotDelegate createBasePlot)
    {
        var plot = createBasePlot(data.Title);  // composed, not inherited
        // add series directly
        return plot;
    }
}
```

```csharp
// ❌ Avoid — protected state shared through inheritance
internal abstract class BaseProcessor
{
    protected readonly ILogger Logger;
    protected int ProcessedCount;

    public abstract Task ProcessAsync();
}

// ✅ Good — pass dependencies as parameters
internal static class OrderProcessor
{
    internal static Task ProcessAsync(ILogger logger) { /* ... */ }
}
```

```csharp
// ✅ Valid — framework base type (not a violation)
internal sealed class HealthCheckController : ControllerBase { /* ... */ }

// ✅ Valid — data hierarchy (not a violation)
public abstract record DomainEvent(DateTime OccurredAt);
public sealed record OrderPlaced(DateTime OccurredAt, OrderId Id) : DomainEvent(OccurredAt);
```

> 🔍 **Audit signature**
>
> 1. A non-abstract class inherits from a non-framework abstract/concrete class AND contains `base.XXX()` calls
> 2. A class overrides `virtual` or `abstract` methods from a non-framework parent — template method pattern
> 3. A class reads or writes `protected` members defined in a non-framework base class
> 4. An `abstract class` that contains method bodies (i.e., shared logic) and is not a framework type — the base class _itself_ is the smell
> 5. A behaviour class inherits from a non-framework, non-data base class at all — the inheritance itself is the smell, even if no `base.` calls are present yet

> **Framework base type (exempt):** any base class defined in a namespace outside your solution (`Microsoft.*`, `System.*`, `MassTransit.*`, third-party libraries). If removing the inheritance would break a framework contract, it qualifies.
