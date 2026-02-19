# Rename JoanComasFdz.Result to PerformanceTester.Functional & Add Option\<T> Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Rename the `JoanComasFdz.Result` project/namespace to `PerformanceTester.Functional` (matching the solution's naming convention), then add a basic `Option<T>` discriminated union with LINQ query syntax support.

**Architecture:** The rename is a mechanical bulk refactor touching ~50 files (directory, csproj, sln, ProjectReferences, `using` statements, docs). The `Option<T>` addition follows the exact same pattern as the existing `Result<TSuccess, TFailure>` — a dunet `[Union]` with convenience properties and LINQ extensions. A new unit test project validates Option behavior.

**Tech Stack:** .NET 9, dunet 1.11.0 (source generator), xUnit, LINQ query syntax

---

## Part 1: Rename JoanComasFdz.Result to PerformanceTester.Functional

### Task 1: Rename directory and project file

**Files:**
- Rename: `src/JoanComasFdz.Result/` -> `src/PerformanceTester.Functional/`
- Rename: `src/JoanComasFdz.Result/JoanComasFdz.Result.csproj` -> `src/PerformanceTester.Functional/PerformanceTester.Functional.csproj`

**Step 1: Rename directory and csproj via git mv**

```bash
cd /workspace/performance-tester-dotnet/src
git mv JoanComasFdz.Result PerformanceTester.Functional
git mv PerformanceTester.Functional/JoanComasFdz.Result.csproj PerformanceTester.Functional/PerformanceTester.Functional.csproj
```

**Step 2: Verify the rename**

```bash
ls /workspace/performance-tester-dotnet/src/PerformanceTester.Functional/
```

Expected: `IsExternalInit.cs  PerformanceTester.Functional.csproj  Result.cs  ResultLinqExtensions.cs  Unit.cs  bin  obj`

---

### Task 2: Update solution file

**Files:**
- Modify: `src/PerformanceTester.sln` (line 54)

**Step 1: Replace the project entry**

Change line 54 from:
```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "JoanComasFdz.Result", "JoanComasFdz.Result\JoanComasFdz.Result.csproj", "{C4E4171C-1157-4CAE-A073-DFB1C79240FB}"
```
To:
```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "PerformanceTester.Functional", "PerformanceTester.Functional\PerformanceTester.Functional.csproj", "{C4E4171C-1157-4CAE-A073-DFB1C79240FB}"
```

Note: The GUID entries in `GlobalSection` (lines 356-367) reference only the GUID `{C4E4171C-...}`, not the project name — they do NOT need updating.

---

### Task 3: Update ProjectReference entries in 6 csproj files

**Files:**
- Modify: `src/PerformanceTester.Common/PerformanceTester.Common.csproj` (line 19)
- Modify: `src/PerformanceTester.Infrastructure/PerformanceTester.Infrastructure.csproj` (line 24)
- Modify: `src/PerformanceTester.ApiLoadTesting/PerformanceTester.ApiLoadTesting.csproj` (line 13)
- Modify: `src/PerformanceTester.Orchestration/PerformanceTester.Orchestration.csproj` (line 36)
- Modify: `src/PerformanceTester.Reporting/PerformanceTester.Reporting.csproj` (line 22)
- Modify: `src/PerformanceTester.Cli/PerformanceTester.Cli.csproj` (line 44)

**Step 1: In each file, replace:**

```xml
<ProjectReference Include="..\JoanComasFdz.Result\JoanComasFdz.Result.csproj" />
```

With:

```xml
<ProjectReference Include="..\PerformanceTester.Functional\PerformanceTester.Functional.csproj" />
```

---

### Task 4: Update namespace declarations in source files

**Files:**
- Modify: `src/PerformanceTester.Functional/Result.cs` (line 3)
- Modify: `src/PerformanceTester.Functional/ResultLinqExtensions.cs` (line 3)
- Modify: `src/PerformanceTester.Functional/Unit.cs` (line 1)

**Step 1: In each file, replace:**

```csharp
namespace JoanComasFdz.Result;
```

With:

```csharp
namespace PerformanceTester.Functional;
```

---

### Task 5: Update all `using` statements in production code

**Step 1: Replace all `using JoanComasFdz.Result;` with `using PerformanceTester.Functional;`**

This is a global find-and-replace across all `.cs` files under `src/`. The affected files (34 files total):

**PerformanceTester.Common (1 file):**
- `src/PerformanceTester.Common/OSPlatformDetector.cs`

**PerformanceTester.Infrastructure (11 files):**
- `src/PerformanceTester.Infrastructure/FindServiceProcessId.cs`
- `src/PerformanceTester.Infrastructure/ValueObjects/ProcessId.cs`
- `src/PerformanceTester.Infrastructure/ValueObjects/Port.cs`
- `src/PerformanceTester.Infrastructure/ValueObjects/NonEmptyString.cs`
- `src/PerformanceTester.Infrastructure/ValueObjects/DatabaseName.cs`
- `src/PerformanceTester.Infrastructure/Database/DatabaseCleaner.cs`
- `src/PerformanceTester.Infrastructure/Database/ClearDatabase.cs`
- `src/PerformanceTester.Infrastructure/RabbitMQ/RabbitMqCleaner.cs`
- `src/PerformanceTester.Infrastructure/RabbitMQ/ClearAllQueues.cs`
- `src/PerformanceTester.Infrastructure/ProcessFinding/WindowsProcessFinder.cs`
- `src/PerformanceTester.Infrastructure/ProcessFinding/LinuxProcessFinder.cs`
- `src/PerformanceTester.Infrastructure/ProcessFinding/ServiceDiscovery.cs`
- `src/PerformanceTester.Infrastructure/ProcessFinding/FindProcessOnPort.cs`

**PerformanceTester.DockerMonitoring (2 files, transitive dependency):**
- `src/PerformanceTester.DockerMonitoring/ValueObjects/RabbitMqContainerName.cs`
- `src/PerformanceTester.DockerMonitoring/ValueObjects/PostgresContainerName.cs`

**PerformanceTester.ApiLoadTesting (2 files):**
- `src/PerformanceTester.ApiLoadTesting/K6Executor.cs`
- `src/PerformanceTester.ApiLoadTesting/K6MetricsParser.cs`

**PerformanceTester.Orchestration (12+ files):**
- `src/PerformanceTester.Orchestration/TestOrchestrator.cs`
- `src/PerformanceTester.Orchestration/ValueObjects/WorkerCount.cs`
- `src/PerformanceTester.Orchestration/ValueObjects/EventCount.cs`
- `src/PerformanceTester.Orchestration/ValueObjects/ApiDuration.cs`
- `src/PerformanceTester.Orchestration/ValueObjects/InactivityTimeout.cs`
- `src/PerformanceTester.Orchestration/ValueObjects/WarmupEventsCount.cs`
- `src/PerformanceTester.Orchestration/ValueObjects/WarmupApiCallsCount.cs`
- `src/PerformanceTester.Orchestration/ValueObjects/Host.cs`
- `src/PerformanceTester.Orchestration/ValueObjects/Username.cs`
- `src/PerformanceTester.Orchestration/ValueObjects/Password.cs`
- `src/PerformanceTester.Orchestration/Phases/WarmupPhase.cs`
- `src/PerformanceTester.Orchestration/Phases/TeardownPhase.cs`
- `src/PerformanceTester.Orchestration/Phases/SetupPhase.cs`
- `src/PerformanceTester.Orchestration/Phases/EventTestPhase.cs`
- `src/PerformanceTester.Orchestration/Phases/ApiTestPhase.cs`
- `src/PerformanceTester.Orchestration/Phases/ReportingPhase.cs`
- `src/PerformanceTester.Orchestration/Phases/PhasesToolbox.cs`

**PerformanceTester.Reporting (4 files):**
- `src/PerformanceTester.Reporting/ValueObjects/FolderPath.cs`
- `src/PerformanceTester.Reporting/ValueObjects/ExistingFolderPath.cs`
- `src/PerformanceTester.Reporting/ValueObjects/ResultsSourceFolder.cs`
- `src/PerformanceTester.Reporting/ValueObjects/ResultsOutputFolder.cs`

**PerformanceTester.Cli (2 files):**
- `src/PerformanceTester.Cli/Configuration/AppConfiguration.cs`
- `src/PerformanceTester.Cli/Commands/TestCommand.cs`

**Step 2: Replace all `using static JoanComasFdz.Result.Result<` with `using static PerformanceTester.Functional.Result<`**

This affects 21 `using static` statements across the files listed above. The types inside the angle brackets do NOT change — only the namespace prefix.

Additionally, replace `JoanComasFdz.Result.Unit` with `PerformanceTester.Functional.Unit` in `using static` lines that reference Unit:
- `src/PerformanceTester.Infrastructure/RabbitMQ/RabbitMqCleaner.cs` — `using static ... Result<JoanComasFdz.Result.Unit, string>` -> `using static ... Result<PerformanceTester.Functional.Unit, string>`
- `src/PerformanceTester.Infrastructure/Database/DatabaseCleaner.cs` — same pattern
- `src/PerformanceTester.Orchestration/Phases/WarmupPhase.cs` — same pattern
- `src/PerformanceTester.Orchestration/Phases/TeardownPhase.cs` — same pattern

---

### Task 6: Update `using` statements in test code

**Files:**
- Modify: `src/PerformanceTester.Infrastructure.IntegrationTests/RabbitMqCleanerTests.cs`
- Modify: `src/PerformanceTester.Infrastructure.IntegrationTests/DatabaseCleanerTests.cs`
- Modify: `src/PerformanceTester.Infrastructure.IntegrationTests/Infrastructure/InfrastructureAssertions.cs`
- Modify: `src/PerformanceTester.Orchestration.IntegrationTests/Infrastructure/TestConfigurationBuilder.cs`
- Modify: `src/PerformanceTester.Orchestration.IntegrationTests/Infrastructure/Orchestration.cs`

**Step 1: Same replacement as Task 5** — `using JoanComasFdz.Result;` -> `using PerformanceTester.Functional;`

---

### Task 7: Update documentation references

**Files:**
- Modify: `CLAUDE.md` (line 75) — architecture diagram
- Modify: `CODING_GUIDELINES.md` (lines 347, 419, 433, 568, 569) — coding guidelines

**Step 1: In `CLAUDE.md`, replace:**

```
│   ├── JoanComasFdz.Result/                      # Result<T> discriminated union (dunet)
```

With:

```
│   ├── PerformanceTester.Functional/              # Result<T>, Option<T>, Unit (dunet)
```

**Step 2: In `CODING_GUIDELINES.md`, replace all 5 occurrences:**

Line 347: `JoanComasFdz.Result` -> `PerformanceTester.Functional`
Line 419: `using static JoanComasFdz.Result.Result<` -> `using static PerformanceTester.Functional.Result<`
Line 433: `using static JoanComasFdz.Result.Result<JoanComasFdz.Result.Unit, string>` -> `using static PerformanceTester.Functional.Result<PerformanceTester.Functional.Unit, string>`
Line 568: `using JoanComasFdz.Result;` -> `using PerformanceTester.Functional;`
Line 569: `using static JoanComasFdz.Result.Result<` -> `using static PerformanceTester.Functional.Result<`

**Step 3: Update Serena memory file** (if it exists):
- `.serena/memories/orchestration_exploration_summary.md` — this file does NOT reference `JoanComasFdz.Result`, only `JoanComasFdz.AssertingThat` (a separate NuGet package that is NOT being renamed). No change needed.

---

### Task 8: Clean, build, and run all tests

**Step 1: Clean old build artifacts**

```bash
cd /workspace/performance-tester-dotnet
rm -rf src/PerformanceTester.Functional/bin src/PerformanceTester.Functional/obj
dotnet clean
```

**Step 2: Build entire solution**

```bash
dotnet build
```

Expected: Build succeeded with 0 errors, 0 warnings.

**Step 3: Run all tests**

```bash
dotnet test
```

Expected: All tests pass. If any fail, investigate — likely a missed `using` statement.

---

### Task 9: Commit the rename

```bash
cd /workspace/performance-tester-dotnet
git add -A
git commit -m "refactor: rename JoanComasFdz.Result to PerformanceTester.Functional

Aligns project naming with the PerformanceTester.* convention used by all
other projects in the solution. No functional changes."
```

---

## Part 2: Add Option\<T>

### Task 10: Implement Option\<T>

**Files:**
- Create: `src/PerformanceTester.Functional/Option.cs`

**Step 1: Create Option\<T>**

Create `src/PerformanceTester.Functional/Option.cs`:

```csharp
using Dunet;

namespace PerformanceTester.Functional;

/// <summary>
/// A discriminated union representing either a value (<see cref="Some"/>) or no value (<see cref="None"/>).
/// Forces callers to explicitly handle both cases — no forgotten null checks.
/// <para>
/// <b>Example — returning an Option:</b>
/// <code>
/// using static Option&lt;User&gt;;
///
/// public static Option&lt;User&gt; FindUser(int id)
/// {
///     var user = db.Users.Find(id);
///     return user is not null
///         ? new Some(user)
///         : new None();
/// }
/// </code>
/// </para>
/// <para>
/// <b>Example — consuming an Option with Match():</b>
/// <code>
/// var option = FindUser(42);
/// option.Match(
///     some: s => Console.WriteLine($"Found: {s.Value.Name}"),
///     none: _ => Console.WriteLine("User not found")
/// );
/// </code>
/// </para>
/// </summary>
/// <typeparam name="T">The type of the value when present.</typeparam>
[Union]
public partial record Option<T>
{
    /// <summary>Represents a present value.</summary>
    partial record Some(T Value);

    /// <summary>Represents the absence of a value.</summary>
    partial record None;

    /// <summary>Returns true if this option contains a value.</summary>
    public bool IsSome => this is Some;

    /// <summary>Returns true if this option contains no value.</summary>
    public bool IsNone => this is None;

    /// <summary>Gets the value. Throws <see cref="InvalidCastException"/> if this is None.</summary>
    public T SomeValue => ((Some)this).Value;
}
```

---

### Task 11: Implement OptionLinqExtensions

**Files:**
- Create: `src/PerformanceTester.Functional/OptionLinqExtensions.cs`

**Step 1: Create OptionLinqExtensions**

Create `src/PerformanceTester.Functional/OptionLinqExtensions.cs`:

```csharp
using System;

namespace PerformanceTester.Functional;

/// <summary>
/// LINQ query syntax support for <see cref="Option{T}"/>.
/// Enables chaining via <c>from...in...select</c> comprehension syntax.
/// <para>
/// <b>Example:</b>
/// <code>
/// var result =
///     from user in FindUser(42)
///     from email in GetEmail(user)
///     select (user, email);
/// </code>
/// The chain short-circuits on the first None.
/// </para>
/// </summary>
public static class OptionLinqExtensions
{
    /// <summary>
    /// Projects the value if present. Enables the <c>select</c> keyword in LINQ queries.
    /// </summary>
    public static Option<U> Select<T, U>(
        this Option<T> option,
        Func<T, U> selector)
    {
        if (option is Option<T>.Some s)
        {
            return new Option<U>.Some(selector(s.Value));
        }

        return new Option<U>.None();
    }

    /// <summary>
    /// Chains a dependent operation. Enables multiple <c>from</c> clauses in LINQ queries.
    /// </summary>
    public static Option<V> SelectMany<T, U, V>(
        this Option<T> option,
        Func<T, Option<U>> bind,
        Func<T, U, V> project)
    {
        if (option is not Option<T>.Some s)
        {
            return new Option<V>.None();
        }

        var bound = bind(s.Value);
        if (bound is Option<U>.Some next)
        {
            return new Option<V>.Some(project(s.Value, next.Value));
        }

        return new Option<V>.None();
    }
}
```

---

### Task 12: Full build and test verification

**Step 1: Build entire solution**

```bash
cd /workspace/performance-tester-dotnet
dotnet build
```

Expected: Build succeeded with 0 errors, 0 warnings.

**Step 2: Run all tests**

```bash
dotnet test
```

Expected: All existing tests still pass.

---

### Task 13: Commit Option\<T>

```bash
cd /workspace/performance-tester-dotnet
git add src/PerformanceTester.Functional/Option.cs \
        src/PerformanceTester.Functional/OptionLinqExtensions.cs
git commit -m "feat: add Option<T> discriminated union with LINQ query syntax

Adds Option<T> (Some/None) following the same dunet pattern as Result<T>.
Includes OptionLinqExtensions for from...in...select comprehension syntax."
```

---

## Summary of Changes

| Category | Old | New |
|----------|-----|-----|
| Directory | `src/JoanComasFdz.Result/` | `src/PerformanceTester.Functional/` |
| Project file | `JoanComasFdz.Result.csproj` | `PerformanceTester.Functional.csproj` |
| Namespace | `JoanComasFdz.Result` | `PerformanceTester.Functional` |
| New types | — | `Option<T>`, `OptionLinqExtensions` |

**Files touched by rename:** ~50 (1 sln, 6 csproj, 3 namespace declarations, ~34 using statements, ~6 using static statements, 2 markdown files)

**New files for Option\<T>:** 2 (Option.cs, OptionLinqExtensions.cs)
