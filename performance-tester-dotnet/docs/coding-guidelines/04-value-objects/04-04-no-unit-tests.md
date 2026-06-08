# 04-04. No Unit Tests for Value Objects

Value object validation logic (range checks, format checks) is trivially correct by inspection. The factory + Result pattern makes invalid construction impossible at compile time. Existing integration and validator tests exercise the parse path indirectly.

```csharp
// ✅ The factory IS the test — invalid values can't exist
EventCount.Create(0)       // → Failure("Events must be between 1 and 1,000,000 (got: 0)")
EventCount.Create(10000)   // → Success(EventCount(10000))
EventCount.Create(1000001) // → Failure("Events must be between 1 and 1,000,000 (got: 1000001)")

// ❌ Avoid - unit tests that restate the range check
[Fact] void Create_WithZero_ReturnsFailure() { ... }  // Just restating the condition
```

**Exception:** If a value object has complex parsing logic (regex, multi-step validation), tests may be warranted.
