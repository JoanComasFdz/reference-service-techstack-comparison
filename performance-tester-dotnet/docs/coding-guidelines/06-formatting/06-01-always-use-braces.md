# 06-01. Always Use Braces in Control Flow Statements

Every `if`, `else`, `for`, `foreach`, `while`, `do`, and `using` must use braces, even when the body is a single line. This prevents bugs when lines are added later and makes the code structure unambiguous.

```csharp
// ✅ Good - braces always present
if (result.IsFailure)
{
    return Fail(TestPhase.Setup, result.FailureError);
}

foreach (var item in items)
{
    Process(item);
}

// ❌ Avoid - braceless single-line body
if (result.IsFailure)
    return Fail(TestPhase.Setup, result.FailureError);

foreach (var item in items)
    Process(item);
```

**Enforced by:** `.editorconfig` rule `csharp_prefer_braces = true:warning`

**No exceptions.** Even guard clauses and early returns use braces. The visual consistency outweighs the marginal brevity.
