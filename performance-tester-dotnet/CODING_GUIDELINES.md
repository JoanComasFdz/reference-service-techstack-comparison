# Coding Guidelines

This document outlines the architectural and design principles used in this codebase, with a focus on functional programming patterns within C#/.NET 9.

The 37 guidelines are organized into 6 focused documents. Load only the document relevant to your current task.

---

## Quick Reference: Which Document to Read

| If you are... | Read |
|---|---|
| Writing a new class or function | [Core Architecture](coding-guidelines/01-core-architecture/01-01-static-classes.md) (Guidelines 01-01 to 01-11) |
| Adding or modifying delegates, wiring dependencies | [Delegates and Dependency Wiring](coding-guidelines/02-delegates-and-dependency-wiring/02-01-named-delegates.md) (Guidelines 02-01 to 02-06) |
| Handling errors, using Result types, or returning optional values | [Error Handling and Absence](coding-guidelines/03-error-handling/03-01-result-over-exceptions.md) (Guidelines 03-01 to 03-05) |
| Creating or modifying value objects, or finding a constructor throw guard for a primitive parameter | [Value Objects](coding-guidelines/04-value-objects/04-01-use-value-objects.md) (Guidelines 04-01 to 04-05) |
| Composing lambdas, managing state, or wrapping framework classes | [State and Composition Patterns](coding-guidelines/05-state-and-composition-patterns/05-01-higher-order-helpers.md) (Guidelines 05-01 to 05-06) |
| Formatting or reviewing code style | [Formatting Rules](coding-guidelines/06-formatting/06-01-always-use-braces.md) (Guidelines 06-01 to 06-04) |

---

## Document Overview

### [01 - Core Architecture](coding-guidelines/01-core-architecture/01-01-static-classes.md)
**Guidelines 01-01 to 01-11.** Foundational structure: static classes for pure logic, explicit parameters, inline single-use code, descriptive names, toolbox pattern, vertical slice ownership, different reasons for change, explicit over implicit, no wrapper functions, composition over interfaces, return early.

### [02 - Delegates and Dependency Wiring](coding-guidelines/02-delegates-and-dependency-wiring/02-01-named-delegates.md)
**Guidelines 02-01 to 02-06.** The complete delegate system: named delegates for single-operation dependencies, named delegates over `Action<T>`/`Func<T>`, interfaces at DI boundaries vs delegates internally, dependency composition with pre-composed capabilities, static class as module (co-located dependencies), delegate suffix convention (`Delegate` suffix).

### [03 - Error Handling and Absence](coding-guidelines/03-error-handling/03-01-result-over-exceptions.md)
**Guidelines 03-01 to 03-05.** Result types instead of exceptions, `using static` for Result construction, dunet `Match` for exhaustive consumption, `IsFailure` + early return for sequential pipelines, `Option<T>` for domain absence vs `T?` for framework interop, no null checks on DI-injected constructor parameters.

### [04 - Value Objects](coding-guidelines/04-value-objects/04-01-use-value-objects.md)
**Guidelines 04-01 to 04-05.** Eliminating primitive obsession: constrained primitives as sealed records, value object structure (`Create()` factory, `ToString()` override), unwrap `.Value` at boundaries, no unit tests for trivial validation, value object families via base record.

### [05 - State and Composition Patterns](coding-guidelines/05-state-and-composition-patterns/05-01-higher-order-helpers.md)
**Guidelines 05-01 to 05-06.** Advanced patterns: higher-order helper functions, behavioral decisions in the consumer, three-bucket rule for lambda binding (bake in / parameter / reader delegate), context record pattern (shared mutable state), immutable state threading (sequential pipelines), thin shell pattern (framework-coupled classes).

### [06 - Formatting Rules](coding-guidelines/06-formatting/06-01-always-use-braces.md)
**Guidelines 06-01 to 06-04.** Always use braces, blank line after closing brace, all-or-nothing parameter wrapping, expression body (`=>`) stays on same line.

---

## Summary Checklist

**One-liner:** _Make dependencies explicit, keep functions small and pure, let each file tell its own complete story._

| # | Principle | Question to Ask |
|---|-----------|----------------|
| 01-01 | Static classes | Does this class have instance state? If no → make it static |
| 01-02 | Explicit parameters | Can I see all inputs at the call site? |
| 01-03 | Inline single-use | Is the name vague AND the body trivial? → Inline or rename to something specific. Does the name precisely label a multi-step operation? → Keep it. |
| 01-04 | Descriptive names | Does the name say exactly what it does? |
| 01-05 | Toolbox pattern | Is this function small, pure, and single-purpose? |
| 01-06 | Vertical slice | Can I understand this file without opening others? |
| 01-07 | Reasons for change | Will these things change together or separately? |
| 01-08 | Explicit over implicit | Do readers need to trace through indirection? |
| 01-09 | No useless wrappers | Does this wrapper add value? |
| 01-10 | Composition over interfaces | Do I actually need this abstraction? |
| 01-11 | Return early | Can I use a guard clause to avoid nesting? |
| 02-01 | Named delegates | Is this dependency a single operation? Use a named delegate |
| 02-02 | Named over Action/Func | Does the delegate name describe what it does? |
| 02-03 | Interfaces vs delegates | Am I at a DI boundary (interface) or internal wiring (delegate)? |
| 02-04 | Dependency composition | Am I receiving interfaces? Contain them in a dependencies class, expose delegates at the right level |
| 02-05 | Static class as module | Can I co-locate delegates, bundle record, factory, and execution in one static class? |
| 02-06 | Delegate suffix | Does the delegate type name end with `Delegate`? |
| 03-01 | Result over exceptions | Is this failure expected? Use Result, not exceptions. Is a constructor throwing for an invalid primitive? → Value Object (04-01), not a Result factory on the class |
| 03-02 | `using static` for Results | Am I producing Results? Shorten with `using static` |
| 03-03 | dunet Match | Am I consuming a Result? Use `Match` (consumption) or `IsFailure` (pipelines) |
| 03-04 | Option for absence | Does this method return "no value" as a domain concept? Use `Option<T>`, not `T?` |
| 03-05 | No null checks in constructors | Is this parameter injected by DI? Assign directly — no `?? throw`, no `ArgumentNullException` |
| 04-01 | Value objects | Does this primitive have domain constraints? Does a constructor throw for an invalid primitive? → Wrap it in a Value Object |
| 04-02 | Value object structure | sealed record, private ctor, `Create()` → Result, `ToString()` |
| 04-03 | Unwrap at boundaries | Am I crossing into a primitive-typed API? Use `.Value` |
| 04-04 | No VO unit tests | Is the validation trivially correct? Skip the test |
| 04-05 | Value object families | Do multiple VOs share the same validation? Base record + sealed tag types |
| 05-01 | Higher-order helpers | Is the same structure repeated with different operations plugged in? |
| 05-02 | Consumer owns defaults | Am I encoding what a consumer needs? Let the consumer decide |
| 05-03 | Three-bucket rule | Is this value fixed at construction, produced at runtime, or mutable? Bake in / parameter / reader delegate |
| 05-04 | Context record pattern | Does this class have mutable state? Extract it into a context record, pass explicitly to static functions |
| 05-05 | Immutable state threading | Is this a single-threaded pipeline? Return new records via `with` / `Aggregate`, no mutation |
| 05-06 | Thin shell pattern | Does this class inherit from a framework base? Own context, wire lifecycle, delegate to static functions |
| 06-01 | Always use braces | Does every `if`/`else`/`for`/`while`/`using` have braces? |
| 06-02 | Blank line after `}` | Is there a blank line after every closing brace (unless followed by another `}`, `else`, `catch`, `finally`)? |
| 06-03 | All-or-nothing params | Are parameters all on one line, or each on its own line? Never partial wrap |
| 06-04 | `=>` same line | Does the expression start on the same line as `=>`? If too long, use block body |
