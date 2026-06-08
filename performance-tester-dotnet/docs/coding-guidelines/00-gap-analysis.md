# Gap Analysis: Individual Rules Not Covered by 00-00 and 00-01

This document identifies content present in the individual leaf rules (01-xx through 06-xx) that is **not covered** by the two summary files:

- **00-00** — Functional Programming Approach (FP concepts)
- **00-01** — Applying FP to C# (C# application patterns)

---

## Completely Missing

These rules or concepts have **zero coverage** in the 00 files.

### Code Hygiene & Style

| Rule | Topic | What's Missing |
|------|-------|----------------|
| **01-03a** | Inline Trivial Single-Use Methods | The mechanical test (single-statement body + called once = inline) and the exception for uniform factory-delegate groups (≥3 siblings with identical two-step body shape) |
| **01-04 / 01-03b** | Specific Names Over Generic Wrappers | The naming quality principle — vague prefixes to watch for (`IsValid`, `Check`, `Process`, `Handle`, `Execute`), the "does the name earn its extraction?" heuristic, and the distinction between domain-meaningful names vs expression-restating names |
| **01-07** | Different Reasons for Change | Separation of concerns: if two things change for different reasons, they belong in different files — even if the code looks similar today |
| **01-11** | Return Early to Avoid Nesting | Guard clauses and early returns to keep the main logic path at base indentation level |

### Architecture & Patterns

| Rule | Topic | What's Missing |
|------|-------|----------------|
| **01-09** | No Single-Implementation Interfaces | The anti-pattern of 1:1 `IFooService`/`FooService` mirror pairs; the mechanical test (exactly one implementing class in the solution); the "test-double justification" rebuttal (inject a delegate instead); the detailed examples showing delegate replacements for both single-method and multi-method interfaces |
| **04-05** | Value Object Families via Base Record | The entire pattern: non-sealed base `record` with `protected` constructor + generic `Create<T>` factory + sealed derived tag types for compile-time swap prevention (e.g., `RabbitMqContainerName` vs `PostgresContainerName` cannot be accidentally swapped) |
| **05-02** | Consumer Owns Defaults | When a consumer receives a shared function with a parameter it doesn't use, the consumer accepts the full signature and provides the default internally — the caller should not encode knowledge about what the consumer does or doesn't care about |

### Error Handling & Testing

| Rule | Topic | What's Missing |
|------|-------|----------------|
| **03-02** | `using static` for Result Construction | The shorthand `using static Result<T, TFailure>` to write `new Success(...)` / `new Failure(...)` instead of repeating the full generic type on every construction |
| **04-04** | No Unit Tests for Value Objects | The testing policy: value object validation is trivially correct by inspection; the factory + Result pattern makes invalid construction impossible; don't write unit tests that merely restate the range check. Exception: complex parsing logic (regex, multi-step validation) may warrant tests |

### Formatting (entire section)

| Rule | Topic | What's Missing |
|------|-------|----------------|
| **06-01** | Always Use Braces | Every `if`/`else`/`for`/`foreach`/`while`/`do`/`using` must use braces, even single-line bodies. No exceptions, including guard clauses. Enforced by `.editorconfig` (`csharp_prefer_braces = true:warning`) |
| **06-02** | Blank Line After Closing Brace | Every `}` followed by a blank line, except before another `}` or `else`/`catch`/`finally`. Not enforced by `.editorconfig` — convention only |
| **06-03** | All-or-Nothing Parameter Wrapping | Parameters all on one line or each on its own line — never partial wrap. Applies to method calls, declarations, constructors, delegates, `new()`, attributes |
| **06-04** | Expression Body Same Line | `=>` expression starts on the same line as the arrow. If too long, switch to block body `{ }`. Exception: delegation patterns where `=>` forwards to another call may have the delegated call on the next line |

---

## Partially Covered

These rules have their core principle mentioned in the 00 files, but important **details, nuances, or specific guidance** are missing.

### 01-08 — Explicit Over Implicit

**Covered:** The FP aspects (Result over exceptions, Option over null, pure functions, explicit dependencies as parameters).

**Missing:** The catalog of specific anti-patterns that represent "implicit" behavior in .NET:
- Convention-based DI registration (assembly scanning)
- Reflection-based mapping (AutoMapper-style `_mapper.Map<T>()`)
- MediatR-style behavior pipelines wired by convention
- Attributes with non-obvious side effects (`[Resilient(retries: 3)]`)
- String-based configuration lookups (`config["Services:Payment:TimeoutMs"]`)
- The "when indirection is acceptable" list (strongly-typed Options, extension methods, higher-order functions, LINQ, `record`/`with`)
- The litmus test: "If I'm reading this call site for the first time, can I understand what happens without knowing a framework convention?"

### 02-02 — Named Over Action/Func

**Covered:** The preference for named delegates over `Func<T>`/`Action<T>` (00-01 section 4).

**Missing:** The **placement guidance** — where to define delegates:
- Next to its data type if shared across callers
- Inside the consumer if only used by one caller

### 02-03 — Interfaces at DI Boundaries, Delegates Internally

**Covered:** 00-01 section 4 says "delegates, not interfaces" broadly.

**Missing:** The **nuanced four-layer table**:

| Layer | Mechanism |
|-------|-----------|
| DI boundary (slice API, multi-method) | Interface |
| DI boundary (single operation) | Named delegate |
| Internal wiring (between static classes) | Named delegate |
| Orchestrator | Lambda adapter (closes over `CancellationToken`, adapts interface → delegate) |

Also missing: the evolution note that single-method interfaces at DI boundaries are being migrated to delegates, and the reasoning that phases stay decoupled because the orchestrator adapts via lambda.

### 04-02 — Value Object Structure

**Covered:** 00-01 section 11 covers Vogen-based value objects extensively.

**Missing:** The **non-Vogen structural template** using `sealed record` with private constructor:
- `using static Result<T, string>` at the top
- Private constructor forcing callers through `Create()`
- `Result<T, string>` for errors (string when callers don't need to branch on failure kinds)
- `Create()` accepting the wider type (e.g., `int` even if internal storage is `ushort`)
- `ToString()` override for string interpolation and structured logging

### 05-01 — Higher-Order Helpers for Structural Duplication

**Covered:** 00-00 section 10 covers higher-order functions generally.

**Missing:** The specific **extraction heuristic**: when two or more call sites share identical structure but plug in different operations, extract the structure as a function that takes a function. When NOT to use: only one call site, or the structure is trivial.

### 05-03 — Three-Bucket Rule for Lambda Binding

**Covered:** 00-01 section 6 covers bake-in (startup values) vs pass-as-parameter (runtime values).

**Missing:** The **third bucket** — reader delegates (`Func<T>` or named delegate) for values that can change during the app's lifetime (feature flags, dynamic settings). Also missing:
- The decision flowchart (does the value exist at construction time? → can it change after construction?)
- The warning about stale closures when baking in a mutable value
- The current codebase examples table mapping specific values to their buckets

### 05-05 — Immutable State Threading (Sequential Pipelines)

**Covered:** 00-00 section 2 covers immutability; 00-01 section 10 covers the mutable Context pattern.

**Missing:** The **companion pattern** for sequential (non-concurrent) pipelines:
- `sealed record` with all immutable properties (positional record)
- Functions return the new state (not `void`)
- Caller uses `Aggregate` or `state = Function(state, input)` pattern
- The decision criteria: use immutable threading for single-threaded pipelines; use mutable Context (05-04) when state is shared across concurrent tasks or contains inherently mutable types

### 05-06 — Thin Shell Pattern (Structural Details)

**Covered:** 00-01 section 1 mentions thin boundary shells; 00-01 section 7 covers vertical slice project structure.

**Missing (extensive):**
- **Visibility-first file structure table:** `Api.cs` (public), `ServiceCollectionExtensions.cs` (public), `ValueObjects/` (public), `Internal/{Concept}Module.cs` (internal), `Internal/{Concept}BackgroundService.cs` (internal)
- **Type visibility rule:** Every type in `Internal/` MUST be `internal` — members CAN be `public` (effectively internal due to containing type)
- **Internal module nesting rule:** Internal types (context record, utilities) nested inside module class; public types (delegates, phase info) in `Api.cs` as top-level types
- **Dunet `[Union]` CS0051 workaround:** `[Union]` types cannot be nested inside an `internal` class — Dunet generates `public` extension methods at namespace level. Place them at namespace level with a `<see cref="...Module"/>` doc comment
- **Foundational library exception:** Projects where nearly everything is public surface (e.g., `Functional` project with `Result<T,E>`, `Option<T>`) don't need `Api.cs` + `Internal/` — use one type per file at root
- **Namespace convention:** `PerformanceTester.{SliceName}` for public, `PerformanceTester.{SliceName}.Internal` for implementation
- **Api.cs reading order:** delegates → phase info (enums + record struct) → data records → public utilities
- **Module file size threshold:** Extract context record first when module exceeds ~500 lines
- **`Internal/` subfolder guidance:** Only add subfolders when a slice has genuinely distinct subsystems; keep flat otherwise
