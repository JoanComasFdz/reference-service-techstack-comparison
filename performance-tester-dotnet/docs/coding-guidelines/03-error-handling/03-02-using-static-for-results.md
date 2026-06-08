# 03-02. Use `using static` to Shorten Result Construction

Producer methods that return `Result<TSuccess, TFailure>` should add a `using static` directive to avoid repeating the full generic type on every `new Success(...)` / `new Failure(...)`.

```csharp
// ✅ Good - using static at the top of the file
using static PerformanceTester.Functional.Result<System.TimeSpan, DurationParseError>;

// Then in the method body:
return new Success(TimeSpan.FromSeconds(value));
return new Failure(new DurationParseError.Empty());

// ❌ Avoid - full type on every construction
return new Result<TimeSpan, DurationParseError>.Success(TimeSpan.FromSeconds(value));
return new Result<TimeSpan, DurationParseError>.Failure(new DurationParseError.Empty());
```

When `TSuccess` or `TFailure` uses types from other namespaces, use fully qualified names in the `using static` directive:

```csharp
using static PerformanceTester.Functional.Result<PerformanceTester.Functional.Unit, string>;
```
