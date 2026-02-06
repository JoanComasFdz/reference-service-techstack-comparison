# Result Pattern with Discriminated Unions

## Summary

Introduce a shared `JoanComasFdz.Result` library using [dunet](https://github.com/domn1995/dunet) to provide the Result pattern with discriminated unions. This eliminates null returns and maximizes use of pattern matching, following the functional direction established in the coding guidelines.

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| DU library | dunet | Source generator, simple attribute-based syntax, exhaustive pattern matching |
| Generic arity | Two union types: `Result<TValue>` and `Result<TValue, TFailure>` | Full type safety; FP-pure approach with smallest library surface |
| Void operations | `Unit` struct used as `Result<Unit>` or `Result<Unit, TFailure>` | Consistent with Rust, F#, Haskell, OCaml, Scala, Kotlin; avoids naming collision |
| Failure type granularity | Per-method or per-domain dunet unions | Each failure variant carries contextual data; defined alongside the method that returns them |
| Target framework | Multi-target `netstandard2.0;net9.0` | Reusable across repos now, extractable to NuGet later |
| Library location | `performance-tester-dotnet/src/JoanComasFdz.Result/` | Inside the solution for now; separate repo + NuGet package later |

## Library Structure

```
performance-tester-dotnet/src/
├── JoanComasFdz.Result/
│   ├── JoanComasFdz.Result.csproj
│   ├── Unit.cs
│   └── Result.cs
├── PerformanceTester.Cli/           (references JoanComasFdz.Result)
├── PerformanceTester.ApiLoadTesting/ (references JoanComasFdz.Result)
└── ...
```

Failure union types live in their respective projects, not in the shared library:

```
PerformanceTester.ApiLoadTesting/
├── K6MetricsParser.cs           # returns Result<K6Metric, ParseLineError>
└── ParseLineError.cs            # [Union] partial record ParseLineError { ... }

PerformanceTester.Cli/Configuration/
├── DurationParser.cs            # returns Result<TimeSpan, DurationParseError>
└── DurationParseError.cs        # [Union] partial record DurationParseError { ... }
```

## Core Types

### Unit.cs

```csharp
namespace JoanComasFdz.Result;

public readonly record struct Unit
{
    public static readonly Unit Value = default;
    public override string ToString() => "()";
}
```

### Result.cs

```csharp
using Dunet;

namespace JoanComasFdz.Result;

[Union]
public partial record Result<TValue>
{
    public partial record Success(TValue Value);
    public partial record Failure(string Message);
}

[Union]
public partial record Result<TValue, TFailure>
{
    public partial record Success(TValue Value);
    public partial record Failure(TFailure Error);
}
```

### Usage Matrix

| | Simple failure (string) | Typed failure (TFailure) |
|--|--|--|
| **Has value** | `Result<TValue>` | `Result<TValue, TFailure>` |
| **Void** | `Result<Unit>` | `Result<Unit, TFailure>` |

## Project Setup

### JoanComasFdz.Result.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>netstandard2.0;net9.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Dunet" Version="1.11.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

Dunet is a source generator (compile-time only). Projects referencing `JoanComasFdz.Result` get the generated Result types without needing dunet themselves. Projects that define their own failure unions need their own dunet reference.

## Failure Type Examples

### Naming Convention

**`{MethodAction}Error`** - e.g., `ParseLineError`, `DurationParseError`, `ClearDatabaseError`.

### K6MetricsParser (PerformanceTester.ApiLoadTesting)

```csharp
[Union]
public partial record ParseLineError
{
    public partial record EmptyInput;
    public partial record InvalidJson(string RawLine);
    public partial record IrrelevantMetric(string MetricName);
    public partial record NonPointMetric(string MetricType);
}
```

### DurationParser (PerformanceTester.Cli)

```csharp
[Union]
public partial record DurationParseError
{
    public partial record Empty;
    public partial record InvalidFormat(string Input);
    public partial record UnknownUnit(char Unit);
}
```

### DatabaseCleaner (PerformanceTester.Infrastructure)

```csharp
[Union]
public partial record ClearDatabaseError
{
    public partial record EmptyName;
    public partial record DatabaseNotFound(string Name);
    public partial record RetriesExhausted(int Attempts, Exception Last);
}
```

## Migration Examples

### Nullable return to Result\<TValue\>

```csharp
// Before: ServiceDiscovery.FindServiceProcessIdAsync
public async Task<int?> FindServiceProcessIdAsync(int port, TimeSpan timeout, ...)
{
    // ... polling logic ...
    return null; // not found
}

// After
public async Task<Result<int>> FindServiceProcessIdAsync(int port, TimeSpan timeout, ...)
{
    // ... polling logic ...
    return new Result<int>.Failure($"No service found on port {port} within {timeout}");
}
```

### Multiple throws to Result\<TValue, TFailure\>

```csharp
// Before: DurationParser.Parse
public static TimeSpan Parse(string duration)
{
    if (string.IsNullOrWhiteSpace(duration))
        throw new ArgumentException("Duration cannot be empty");
    if (!regex.IsMatch(duration))
        throw new ArgumentException($"Invalid duration format: '{duration}'");
    // ... switch on unit, throws on unknown
}

// After
public static Result<TimeSpan, DurationParseError> Parse(string duration)
{
    if (string.IsNullOrWhiteSpace(duration))
        return new Result<TimeSpan, DurationParseError>.Failure(new DurationParseError.Empty());
    var match = regex.Match(duration);
    if (!match.Success)
        return new Result<TimeSpan, DurationParseError>.Failure(new DurationParseError.InvalidFormat(duration));
    // ... switch on unit, returns UnknownUnit variant
}
```

### Void method to Result\<Unit\>

```csharp
// Before: RabbitMqCleaner.ClearAllQueuesAsync
public async Task ClearAllQueuesAsync(CancellationToken cancellationToken = default)

// After
public async Task<Result<Unit>> ClearAllQueuesAsync(CancellationToken cancellationToken = default)
{
    // ... on success:
    return new Result<Unit>.Success(Unit.Value);
    // ... on failure:
    return new Result<Unit>.Failure($"Failed to purge {failureCount} queues");
}
```

### Pattern Matching at Call Sites

```csharp
var result = parser.ParseLine(line);
var message = result.Match(
    success => $"Parsed: {success.Value.Metric}",
    failure => failure.Error switch
    {
        ParseLineError.EmptyInput => "Skipping empty line",
        ParseLineError.InvalidJson e => $"Bad JSON: {e.RawLine}",
        ParseLineError.IrrelevantMetric e => $"Ignoring: {e.MetricName}",
        ParseLineError.NonPointMetric e => $"Wrong type: {e.MetricType}",
    }
);
```

## Testing

```csharp
[Fact]
public void Parse_EmptyString_ReturnsEmpty()
{
    var result = DurationParser.Parse("");

    var failure = Assert.IsType<Result<TimeSpan, DurationParseError>.Failure>(result);
    Assert.IsType<DurationParseError.Empty>(failure.Error);
}

[Fact]
public void Parse_ValidDuration_ReturnsTimeSpan()
{
    var result = DurationParser.Parse("30s");

    var success = Assert.IsType<Result<TimeSpan, DurationParseError>.Success>(result);
    Assert.Equal(TimeSpan.FromSeconds(30), success.Value);
}
```

## Migration Strategy

Migration is **incremental** - each method can be converted independently. No big-bang refactor required. Priority candidates identified from codebase analysis:

1. `K6MetricsParser.ParseLine()` - 4 null reasons collapsed into `null`
2. `DurationParser.Parse()` - 3 throws with same exception type
3. `ServiceDiscovery.FindServiceProcessIdAsync()` - nullable `int?` hiding "not found"
4. `DatabaseCleaner.ClearDatabaseAsync()` - 3 distinct failure types
5. `RabbitMqCleaner.GetAllQueuesAsync()` - empty list hiding connection errors
