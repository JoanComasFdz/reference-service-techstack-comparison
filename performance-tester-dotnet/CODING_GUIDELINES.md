# Coding Guidelines

This document outlines the architectural and design principles used in this codebase, with a focus on functional programming patterns within C#/.NET 9.

The 35 guidelines are organized into 6 focused documents. Load only the document relevant to your current task.

---

## Quick Reference: Which Document to Read

| If you are... | Read |
|---|---|
| Writing a new class or function | [Core Architecture](coding-guidelines/01-core-architecture.md) (Guidelines 1-11) |
| Adding or modifying delegates, wiring dependencies | [Delegates and Dependency Wiring](coding-guidelines/02-delegates-and-dependency-wiring.md) (Guidelines 12-14, 29-30, 35) |
| Handling errors or using Result types | [Error Handling](coding-guidelines/03-error-handling.md) (Guidelines 15-17) |
| Creating or modifying value objects | [Value Objects](coding-guidelines/04-value-objects.md) (Guidelines 18-22) |
| Composing lambdas, managing state, or wrapping framework classes | [State and Composition Patterns](coding-guidelines/05-state-and-composition-patterns.md) (Guidelines 23-24, 31-34) |
| Formatting or reviewing code style | [Formatting Rules](coding-guidelines/06-formatting.md) (Guidelines 25-28) |

---

## Document Overview

### [01 - Core Architecture](coding-guidelines/01-core-architecture.md)
**Guidelines 1-11.** Foundational structure: static classes for pure logic, explicit parameters, inline single-use code, descriptive names, toolbox pattern, vertical slice ownership, different reasons for change, explicit over implicit, no wrapper functions, composition over interfaces, return early.

### [02 - Delegates and Dependency Wiring](coding-guidelines/02-delegates-and-dependency-wiring.md)
**Guidelines 12-14, 29-30, 35.** The complete delegate system: named delegates for single-operation dependencies, named delegates over `Action<T>`/`Func<T>`, interfaces at DI boundaries vs delegates internally, dependency composition with pre-composed capabilities, static class as module (co-located dependencies), delegate suffix convention (`Delegate` suffix).

### [03 - Error Handling](coding-guidelines/03-error-handling.md)
**Guidelines 15-17.** Result types instead of exceptions, `using static` for Result construction, dunet `Match` for exhaustive consumption, `IsFailure` + early return for sequential pipelines.

### [04 - Value Objects](coding-guidelines/04-value-objects.md)
**Guidelines 18-22.** Eliminating primitive obsession: constrained primitives as sealed records, value object structure (`Create()` factory, `ToString()` override), unwrap `.Value` at boundaries, no unit tests for trivial validation, value object families via base record.

### [05 - State and Composition Patterns](coding-guidelines/05-state-and-composition-patterns.md)
**Guidelines 23-24, 31-34.** Advanced patterns: higher-order helper functions, behavioral decisions in the consumer, three-bucket rule for lambda binding (bake in / parameter / reader delegate), context record pattern (shared mutable state), immutable state threading (sequential pipelines), thin shell pattern (framework-coupled classes).

### [06 - Formatting Rules](coding-guidelines/06-formatting.md)
**Guidelines 25-28.** Always use braces, blank line after closing brace, all-or-nothing parameter wrapping, expression body (`=>`) stays on same line.

---

## Summary Checklist

**One-liner:** _Make dependencies explicit, keep functions small and pure, let each file tell its own complete story._

| # | Principle | Question to Ask |
|---|-----------|----------------|
| 1 | Static classes | Does this class have instance state? If no → make it static |
| 2 | Explicit parameters | Can I see all inputs at the call site? |
| 3 | Inline single-use | Is this only used once? Inline it with a comment |
| 4 | Descriptive names | Does the name say exactly what it does? |
| 5 | Toolbox pattern | Is this function small, pure, and single-purpose? |
| 6 | Vertical slice | Can I understand this file without opening others? |
| 7 | Reasons for change | Will these things change together or separately? |
| 8 | Explicit over implicit | Do readers need to trace through indirection? |
| 9 | No useless wrappers | Does this wrapper add value? |
| 10 | Composition over interfaces | Do I actually need this abstraction? |
| 11 | Return early | Can I use a guard clause to avoid nesting? |
| 12 | Named delegates | Is this dependency a single operation? Use a named delegate |
| 13 | Named over Action/Func | Does the delegate name describe what it does? |
| 14 | Interfaces vs delegates | Am I at a DI boundary (interface) or internal wiring (delegate)? |
| 15 | Result over exceptions | Is this failure expected? Use Result, not exceptions |
| 16 | `using static` for Results | Am I producing Results? Shorten with `using static` |
| 17 | dunet Match | Am I consuming a Result? Use `Match` (consumption) or `IsFailure` (pipelines) |
| 18 | Value objects | Does this primitive have domain constraints? Wrap it |
| 19 | Value object structure | sealed record, private ctor, `Create()` → Result, `ToString()` |
| 20 | Unwrap at boundaries | Am I crossing into a primitive-typed API? Use `.Value` |
| 21 | No VO unit tests | Is the validation trivially correct? Skip the test |
| 22 | Value object families | Do multiple VOs share the same validation? Base record + sealed tag types |
| 23 | Higher-order helpers | Is the same structure repeated with different operations plugged in? |
| 24 | Consumer owns defaults | Am I encoding what a consumer needs? Let the consumer decide |
| 25 | Always use braces | Does every `if`/`else`/`for`/`while`/`using` have braces? |
| 26 | Blank line after `}` | Is there a blank line after every closing brace (unless followed by another `}`, `else`, `catch`, `finally`)? |
| 27 | All-or-nothing params | Are parameters all on one line, or each on its own line? Never partial wrap |
| 28 | `=>` same line | Does the expression start on the same line as `=>`? If too long, use block body |
| 29 | Dependency composition | Am I receiving interfaces? Contain them in a dependencies class, expose delegates at the right level |
| 30 | Static class as module | Can I co-locate delegates, bundle record, factory, and execution in one static class? |
| 31 | Three-bucket rule | Is this value fixed at construction, produced at runtime, or mutable? Bake in / parameter / reader delegate |
| 32 | Context record pattern | Does this class have mutable state? Extract it into a context record, pass explicitly to static functions |
| 33 | Immutable state threading | Is this a single-threaded pipeline? Return new records via `with` / `Aggregate`, no mutation |
| 34 | Thin shell pattern | Does this class inherit from a framework base? Own context, wire lifecycle, delegate to static functions |
| 35 | Delegate suffix | Does the delegate type name end with `Delegate`? |
