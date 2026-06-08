# 01-05. Toolbox Pattern — Reusable Pure Functions

> **Scope:** This is a design principle for human developers and PR reviewers, particularly when evaluating proposals to extract shared builders or base classes. It is not subject to automated audit because deciding when to extract, what constitutes duplication, and where module boundaries fall are inherently context-dependent judgments.

In a functional C# codebase, code reuse comes from **small, pure functions grouped in static toolbox classes**. Each function operates only on its parameters and returns a result — no fields, no DI, no I/O.

Name toolbox classes with a domain prefix and the `*Toolbox` suffix (e.g., `PlotToolbox`, `ParsingToolbox`, `MoneyToolbox`). Avoid generic names like `Helpers`, `Utils`, or `Common` — they attract unrelated methods and erode cohesion.

Callers bind to toolbox functions in two ways:

## Strategy 1: Direct Call

The caller references the toolbox class and method explicitly. Simple, discoverable, appropriate when caller and toolbox live in the same module.

```csharp
internal static class PlotToolbox
{
    public static double[] ExtractTimestamps(IReadOnlyList<ResourceSampleJson> samples) =>
        samples.Select(s => s.Timestamp.ToOADate()).ToArray();

    public static double[] ExtractCpuValues(IReadOnlyList<ResourceSampleJson> samples) =>
        samples.Select(s => s.CpuPercent).ToArray();

    public static void AddScatterWithFill(Plot plot, double[] x, double[] y, Color color) => ...
    public static void AddAverageLine(Plot plot, double value, Color color) => ...
}
```

```csharp
internal static class ServiceMetricsPlotBuilder
{
    public static Plot Build(ResourceMetricsReport data, ChartConfig config)
    {
        var plot = new Plot();
        var timestamps = PlotToolbox.ExtractTimestamps(data.Samples);

        PlotToolbox.AddScatterWithFill(plot, timestamps, PlotToolbox.ExtractCpuValues(data.Samples), ...);
        PlotToolbox.AddAverageLine(plot, data.CpuSummary.Avg, ...);

        return plot;
    }
}
```

## Strategy 2: Delegate Indirection

The toolbox defines the function once, but callers receive it as a `Func<>` or custom delegate — no direct dependency on the toolbox class. More FP-idiomatic, better for testability, and keeps callers decoupled from the concrete implementation.

```csharp
internal static class ParsingToolbox
{
    public static Result<Money> ParseMoney(string raw) => ...
    public static Result<DateOnly> ParseDate(string raw) => ...
}
```

A composition root or wiring layer binds the delegate once:

```csharp
// Registration — the only place that knows about ParsingToolbox
services.AddSingleton<Func<string, Result<Money>>>(_ => ParsingToolbox.ParseMoney);
services.AddSingleton<Func<string, Result<DateOnly>>>(_ => ParsingToolbox.ParseDate);
```

Callers depend on the delegate, not the toolbox:

```csharp
internal class ImportHandler(Func<string, Result<Money>> parseMoney)
{
    public Result<ImportedRow> Handle(RawRow row) =>
        parseMoney(row.Amount).Map(money => new ImportedRow(row.Id, money));
}
```

## When to Use Which Strategy

**Use direct calls when:**

- Caller and toolbox live in the same module
- The function is a simple transformation unlikely to need substitution
- Discoverability and readability matter more than decoupling

**Use delegate indirection when:**

- The caller belongs to a different module or bounded context
- You want to substitute the implementation in tests without referencing the toolbox
- Multiple implementations of the same shape exist and the caller shouldn't choose

## What Belongs in a Toolbox

A toolbox function must be **pure** — it reads only its parameters and produces a return value. If a function needs any of the following, it is a **service**, not a toolbox function:

- Injected dependencies (`ILogger`, `IRepository`, etc.)
- Mutable static or instance state
- I/O (file, network, database)

Extract to a toolbox when:

- Two or more callers need the same transformation
- A pure function represents a standalone domain concept worth naming (e.g., `ApplyDiscount`, `ParseMoney`), even if there's only one caller today
