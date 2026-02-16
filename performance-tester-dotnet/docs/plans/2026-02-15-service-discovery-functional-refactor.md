# ServiceDiscovery Functional Refactor Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Refactor the Infrastructure slice's ServiceDiscovery component from traditional OOP (sealed class + constructor injection + internal interface) to the functional pattern established by the Orchestration layer (static classes + named delegates + explicit parameters), while leveraging the existing `Port` value object to eliminate duplicate validation and adopting Result types for all known failure scenarios.

**Architecture:** The existing `Port` value object moves from Orchestration to Infrastructure (port validation is an infrastructure concern, and Orchestration already depends on Infrastructure). The public `IServiceDiscovery` interface is replaced by a `FindServiceProcessId` named delegate that accepts `Port` instead of `int` — making invalid states unrepresentable ("parse don't validate", Guideline 22). The internal `IProcessFinder` interface is replaced by a `FindProcessOnPort` named delegate (Guideline 12). `LinuxProcessFinder` and `WindowsProcessFinder` become static classes with explicit logger parameters (Guidelines 1, 2). The core polling logic in `ServiceDiscovery` becomes a static method that receives a `Port` (guaranteed valid by the VO). The DI factory closure wires the static core directly — no adapter class needed. `OSPlatformDetector` returns `Result<SupportedPlatform, string>` instead of throwing. The `ServiceCollectionExtensions.AddInfrastructure()` method becomes the composition root that wires delegates via closures and throws for unsupported platforms (deployment error — the app cannot function without a process finder).

**Tech Stack:** .NET 9, C# 13, xUnit, JoanComasFdz.Result

**Guidelines Applied:**

- **#1** Static Classes for Pure Logic — process finders and core discovery logic have no instance state
- **#2** Explicit Parameters Over Hidden State — logger and findProcessOnPort passed as method parameters
- **#12** Named Delegates for Single-Operation Dependencies — `FindProcessOnPort` replaces `IProcessFinder`, `FindServiceProcessId` replaces `IServiceDiscovery`
- **#15** Result Types Instead of Exceptions — unsupported platform returns `Failure`
- **#16** `using static` to Shorten Result Construction
- **#18–22** Value Objects — `Port` VO eliminates invalid-port scenario at the type level
- **#22** Parse Don't Validate — `FindServiceProcessId` accepts `Port`, not `int`; invalid states are unrepresentable
- **#25** Always Use Braces — all control flow in new/modified code
- **#26** Blank Line After Closing Brace
- **#27** All-or-Nothing Parameter Wrapping
- **#28** Expression Body Stays on Same Line as `=>`

---

## Exception Elimination Summary

| Exception                                          | Location                                          | Replaced With                                                                                    |
| -------------------------------------------------- | ------------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| `ArgumentOutOfRangeException` for invalid port     | `ServiceDiscovery.FindServiceProcessIdAsync`      | Eliminated — `Port` value object makes invalid states unrepresentable (Guideline 22)             |
| `PlatformNotSupportedException` for unsupported OS | `OSPlatformDetector.GetCurrentPlatform()`         | Returns `Result<SupportedPlatform, string>` with `Failure` for unsupported platform              |
| `PlatformNotSupportedException` in DI registration | `ServiceCollectionExtensions.AddInfrastructure()` | Remains — unsupported platform at startup is a deployment error, not a runtime business scenario |

**Exceptions that remain (bugs/deployment errors, not domain failures):**

- `PlatformNotSupportedException` in `AddInfrastructure()` — unsupported platform at startup is a deployment error. The app cannot function without a process finder. `OSPlatformDetector` returns `Result`, but the composition root decides this is fatal.

(No `UnreachableException` needed — `SupportedPlatform` is a Dunet union with exhaustive `Match`, so missing cases are compile-time errors.)

---

## File Change Summary

| File                                                                                               | Action                                                             |
| -------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------ |
| `src/PerformanceTester.Orchestration/ValueObjects/Port.cs`                                         | MOVE → `src/PerformanceTester.Infrastructure/ValueObjects/Port.cs` |
| `src/PerformanceTester.Orchestration/TestConfiguration.cs`                                         | MODIFY (add `using PerformanceTester.Infrastructure.ValueObjects`) |
| `src/PerformanceTester.Orchestration/Phases/SetupPhaseDependencies.cs`                             | MODIFY (pass `config.ServicePort` directly)                        |
| `src/PerformanceTester.Orchestration/TestReportBuilder.cs`                                         | MODIFY (add `using PerformanceTester.Infrastructure.ValueObjects`) |
| `src/PerformanceTester.Cli/Commands/TestCommand.cs`                                                | MODIFY (change `using` from Orchestration to Infrastructure)       |
| `src/PerformanceTester.Cli/Configuration/AppConfiguration.cs`                                      | MODIFY (change `using` from Orchestration to Infrastructure)       |
| `src/PerformanceTester.Orchestration.IntegrationTests/Infrastructure/TestConfigurationBuilder.cs`  | MODIFY (add `using PerformanceTester.Infrastructure.ValueObjects`) |
| `src/PerformanceTester.Common/PerformanceTester.Common.csproj`                                     | MODIFY (add `JoanComasFdz.Result` project reference + Dunet)       |
| `src/PerformanceTester.Common/SupportedPlatform.cs`                                                | MODIFY (enum → Dunet union for exhaustive `Match`)                 |
| `src/PerformanceTester.Common/OSPlatformDetector.cs`                                               | MODIFY (return `Result<SupportedPlatform, string>`)                |
| `src/PerformanceTester.Infrastructure/IServiceDiscovery.cs`                                        | DELETE → replaced by `FindServiceProcessId.cs` named delegate      |
| `src/PerformanceTester.Infrastructure/FindServiceProcessId.cs`                                     | CREATE (named delegate, accepts `Port`)                            |
| `src/PerformanceTester.Infrastructure/ProcessFinding/IProcessFinder.cs`                            | DELETE                                                             |
| `src/PerformanceTester.Infrastructure/ProcessFinding/FindProcessOnPort.cs`                         | CREATE (named delegate, accepts `Port`)                            |
| `src/PerformanceTester.Infrastructure/ProcessFinding/LinuxProcessFinder.cs`                        | MODIFY → static class                                              |
| `src/PerformanceTester.Infrastructure/ProcessFinding/WindowsProcessFinder.cs`                      | MODIFY → static class                                              |
| `src/PerformanceTester.Infrastructure/ProcessFinding/ServiceDiscovery.cs`                          | MODIFY → static class, accepts `Port`                              |
| `src/PerformanceTester.Infrastructure.IntegrationTests/Infrastructure/Infrastructure.cs`           | MODIFY (resolve `FindServiceProcessId` delegate)                   |
| `src/PerformanceTester.Infrastructure/ServiceCollectionExtensions.cs`                              | MODIFY (new composition, Dunet `Match`, throws on unsupported OS)  |
| `src/PerformanceTester.Infrastructure.IntegrationTests/ServiceDiscoveryTests.cs`                   | MODIFY (use `Port`, remove invalid-port test)                      |
| `src/PerformanceTester.Infrastructure.IntegrationTests/Infrastructure/InfrastructureAssertions.cs` | MODIFY (accept `Port`, remove invalid-port assertion)              |

---

## Task 1: Verify Baseline

**Files:** None (read-only)

**Step 1: Run existing Infrastructure integration tests**

Run:

```bash
cd /workspace/performance-tester-dotnet && dotnet test src/PerformanceTester.Infrastructure.IntegrationTests --filter "FullyQualifiedName~ServiceDiscoveryTests" --logger "console;verbosity=detailed"
```

Expected: All 3 tests pass:

- `FindServiceProcessIdAsync_WhenServiceIsRunning_ShouldReturnProcessId` — PASS
- `FindServiceProcessIdAsync_WhenNoServiceOnPort_ShouldReturnNull` — PASS
- `FindServiceProcessIdAsync_WhenInvalidPort_ShouldThrowArgumentOutOfRangeException` — PASS

**Step 2: Run full build to confirm zero warnings**

Run:

```bash
cd /workspace/performance-tester-dotnet && dotnet build src/PerformanceTester.Infrastructure 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

---

## Task 2: Move Port Value Object to Infrastructure

Port validation (1–65535) is an infrastructure concern. The `Port` value object currently lives in `PerformanceTester.Orchestration.ValueObjects`, but the `FindServiceProcessId` delegate (in Infrastructure) needs to accept `Port` as a parameter. Since Infrastructure can't depend on Orchestration (dependency flows the other way), Port moves to Infrastructure. Orchestration already depends on Infrastructure, so all existing callers retain access.

**Files:**

- Move: `src/PerformanceTester.Orchestration/ValueObjects/Port.cs` → `src/PerformanceTester.Infrastructure/ValueObjects/Port.cs`
- Modify: `src/PerformanceTester.Infrastructure/ValueObjects/Port.cs` (update namespace)
- Modify: `src/PerformanceTester.Orchestration/TestConfiguration.cs` (add import)
- Modify: `src/PerformanceTester.Orchestration/TestReportBuilder.cs` (add import if it uses Port)
- Modify: `src/PerformanceTester.Cli/Commands/TestCommand.cs` (change import)
- Modify: `src/PerformanceTester.Cli/Configuration/AppConfiguration.cs` (change import)
- Modify: `src/PerformanceTester.Orchestration.IntegrationTests/Infrastructure/TestConfigurationBuilder.cs` (add import)

### Step 1: Move Port.cs to Infrastructure

Move the file from `src/PerformanceTester.Orchestration/ValueObjects/Port.cs` to `src/PerformanceTester.Infrastructure/ValueObjects/Port.cs`.

```bash
mkdir -p /workspace/performance-tester-dotnet/src/PerformanceTester.Infrastructure/ValueObjects
git mv src/PerformanceTester.Orchestration/ValueObjects/Port.cs src/PerformanceTester.Infrastructure/ValueObjects/Port.cs
```

### Step 2: Update Port.cs namespace and `using static`

Replace the namespace and `using static` in `src/PerformanceTester.Infrastructure/ValueObjects/Port.cs`:

```csharp
using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Infrastructure.ValueObjects.Port, string>;

namespace PerformanceTester.Infrastructure.ValueObjects;

public sealed record Port
{
    public int Value { get; }
    private Port(int value) => Value = value;

    public static Result<Port, string> Create(int value) => value is >= 1 and <= 65535
            ? new Success(new Port(value))
            : new Failure($"Port must be between 1 and 65535 (got: {value})");

    public static Result<Port, string> Create(string value) => int.TryParse(value, out var parsed)
            ? Create(parsed)
            : new Failure($"Port must be a number (got: '{value}')");

    public static Port FromInt(int value) => new(value);

    public override string ToString() => Value.ToString();
}
```

### Step 3: Update imports in Orchestration and CLI files

Files that reference `Port` need to add `using PerformanceTester.Infrastructure.ValueObjects;`. Files that also use other Orchestration VOs keep their existing `using PerformanceTester.Orchestration.ValueObjects;`.

**`src/PerformanceTester.Orchestration/TestConfiguration.cs`** — add import:

```csharp
using PerformanceTester.Infrastructure.ValueObjects;
using PerformanceTester.Orchestration.ValueObjects;
using PerformanceTester.Reporting.ValueObjects;
```

**`src/PerformanceTester.Cli/Commands/TestCommand.cs`** — add import:

```csharp
using PerformanceTester.Infrastructure.ValueObjects;
```

(Keep existing `using PerformanceTester.Orchestration.ValueObjects;` for other VOs like EventCount, ApiDuration, etc.)

**`src/PerformanceTester.Cli/Configuration/AppConfiguration.cs`** — add import:

```csharp
using PerformanceTester.Infrastructure.ValueObjects;
```

(Keep existing `using PerformanceTester.Orchestration.ValueObjects;` for other VOs.)

**`src/PerformanceTester.Orchestration.IntegrationTests/Infrastructure/TestConfigurationBuilder.cs`** — add import:

```csharp
using PerformanceTester.Infrastructure.ValueObjects;
```

(Keep existing `using PerformanceTester.Orchestration.ValueObjects;` for other VOs.)

Check `src/PerformanceTester.Orchestration/TestReportBuilder.cs` — if it references `Port`, add the import. If it only uses `config.ServicePort.Value` (accessing the int), the `Port` type is used via `TestConfiguration` and the import is needed.

### Step 4: Build full solution to verify the move

Run:

```bash
cd /workspace/performance-tester-dotnet && dotnet build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` — the move is namespace-only, no behavior change.

### Step 5: Run full test suite to confirm no regressions

Run:

```bash
cd /workspace/performance-tester-dotnet && dotnet test
```

Expected: All tests pass. The Port VO behavior is identical, only the namespace changed.

### Step 6: Commit

```bash
git add -A && git commit -m "refactor: move Port value object from Orchestration to Infrastructure

Port validation (1-65535) is an infrastructure concern. Moving Port to
Infrastructure allows FindServiceProcessId to accept Port as a parameter
(parse don't validate, Guideline 22). Orchestration depends on
Infrastructure, so all existing callers retain access.

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>"
```

---

## Task 3: Change SupportedPlatform to Dunet Union and OSPlatformDetector to Return Result

This task makes two related changes to `PerformanceTester.Common`:

1. **`SupportedPlatform`** changes from an `enum` to a Dunet `[Union]` — this gives exhaustive `Match` at compile time, eliminating the need for `UnreachableException` default arms in switch expressions.
2. **`OSPlatformDetector`** changes from throwing to returning `Result<SupportedPlatform, string>` for unsupported platforms.

Both Dunet (source generator, compile-time only) and `JoanComasFdz.Result` (multi-targets `netstandard2.0`) are compatible with Common's target framework.

**Files:**

- Modify: `src/PerformanceTester.Common/PerformanceTester.Common.csproj` (add Result reference + Dunet)
- Modify: `src/PerformanceTester.Common/SupportedPlatform.cs` (enum → Dunet union)
- Modify: `src/PerformanceTester.Common/OSPlatformDetector.cs`

### Step 1: Add `JoanComasFdz.Result` project reference and Dunet package to Common

In `src/PerformanceTester.Common/PerformanceTester.Common.csproj`, add both references:

```xml
  <ItemGroup>
    <PackageReference Include="Dunet" Version="1.11.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\JoanComasFdz.Result\JoanComasFdz.Result.csproj" />
  </ItemGroup>
```

### Step 2: Change `SupportedPlatform` from enum to Dunet union

Replace `src/PerformanceTester.Common/SupportedPlatform.cs` with:

```csharp
using Dunet;

namespace PerformanceTester.Common;

/// <summary>
/// Supported operating system platforms.
/// Dunet union provides exhaustive <see cref="Match"/> — missing cases are compile-time errors.
/// </summary>
[Union]
public partial record SupportedPlatform
{
    /// <summary>Windows operating system.</summary>
    public partial record Windows;

    /// <summary>Linux operating system.</summary>
    public partial record Linux;
}
```

### Step 3: Modify `OSPlatformDetector.GetCurrentPlatform()` to return `Result<SupportedPlatform, string>`

Replace `src/PerformanceTester.Common/OSPlatformDetector.cs` with:

```csharp
using System.Runtime.InteropServices;
using JoanComasFdz.Result;
using static JoanComasFdz.Result.Result<PerformanceTester.Common.SupportedPlatform, string>;

namespace PerformanceTester.Common;

/// <summary>
/// Detects the current operating system platform.
/// Uses RuntimeInformation to detect Windows or Linux.
/// </summary>
public static class OSPlatformDetector
{
    /// <summary>
    /// Gets the current operating system platform.
    /// </summary>
    /// <returns>Success with the detected platform (Windows or Linux), or Failure if the platform is not supported.</returns>
    public static Result<SupportedPlatform, string> GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new Success(new SupportedPlatform.Linux());
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new Success(new SupportedPlatform.Windows());
        }

        return new Failure(
            $"Platform {RuntimeInformation.OSDescription} is not supported. Only Linux and Windows are supported.");
    }
}
```

### Step 3: Build Common project

Run:

```bash
cd /workspace/performance-tester-dotnet && dotnet build src/PerformanceTester.Common
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

### Step 4: Build Infrastructure to check the caller

Run:

```bash
cd /workspace/performance-tester-dotnet && dotnet build src/PerformanceTester.Infrastructure
```

Expected: Build **fails** because `ServiceCollectionExtensions` uses `var platform = OSPlatformDetector.GetCurrentPlatform()` in a `switch` that expects `SupportedPlatform`, but now gets `Result<SupportedPlatform, string>`. Also, `SupportedPlatform` is no longer an enum so the `case` arms don't compile. This is expected — we fix it in Task 5.

### Step 6: Commit

```bash
git add src/PerformanceTester.Common/PerformanceTester.Common.csproj src/PerformanceTester.Common/SupportedPlatform.cs src/PerformanceTester.Common/OSPlatformDetector.cs && git commit -m "refactor: make SupportedPlatform a Dunet union and return Result from OSPlatformDetector

SupportedPlatform changes from enum to Dunet union — exhaustive Match
eliminates UnreachableException default arms at compile time.
OSPlatformDetector returns Result<SupportedPlatform, string> instead of
throwing PlatformNotSupportedException (Guideline 15). Add Dunet and
JoanComasFdz.Result references to Common.

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>"
```

---

## Task 4: Create Named Delegate and Refactor Process Finders to Static

This task replaces the `IProcessFinder` interface with a `FindProcessOnPort` named delegate and makes both platform-specific finders static classes with explicit parameters.

**Files:**

- Create: `src/PerformanceTester.Infrastructure/ProcessFinding/FindProcessOnPort.cs`
- Modify: `src/PerformanceTester.Infrastructure/ProcessFinding/LinuxProcessFinder.cs`
- Modify: `src/PerformanceTester.Infrastructure/ProcessFinding/WindowsProcessFinder.cs`
- Delete: `src/PerformanceTester.Infrastructure/ProcessFinding/IProcessFinder.cs`

### Step 1: Create the `FindProcessOnPort` named delegate

Create file `src/PerformanceTester.Infrastructure/ProcessFinding/FindProcessOnPort.cs`:

```csharp
using JoanComasFdz.Result;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.Infrastructure.ProcessFinding;

/// <summary>
/// Platform-specific process finder for discovering PIDs on ports.
/// Named delegate replacing IProcessFinder interface (Guideline 12).
/// Returns Success with PID if found, Failure with reason otherwise (Guideline 15).
/// </summary>
internal delegate Task<Result<int, string>> FindProcessOnPort(Port port, CancellationToken cancellationToken);
```

### Step 2: Refactor `LinuxProcessFinder` to static class

Replace `src/PerformanceTester.Infrastructure/ProcessFinding/LinuxProcessFinder.cs` with:

```csharp
using System.Diagnostics;
using System.Text.RegularExpressions;
using JoanComasFdz.Result;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;
using static JoanComasFdz.Result.Result<int, string>;

namespace PerformanceTester.Infrastructure.ProcessFinding;

/// <summary>
/// Linux-specific process finder using lsof or ss command.
/// Tries lsof first, falls back to ss if lsof is not available.
/// </summary>
internal static partial class LinuxProcessFinder
{
    /// <summary>
    /// Finds the process ID listening on the specified port using Linux tools.
    /// </summary>
    public static async Task<Result<int, string>> FindProcessOnPortAsync(
        Port port,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Try lsof first (most reliable)
        var pid = await TryFindWithLsofAsync(port, logger, cancellationToken);
        if (pid.HasValue)
        {
            return new Success(pid.Value);
        }

        // Fallback to ss (available in most Linux distributions)
        pid = await TryFindWithSsAsync(port, logger, cancellationToken);
        if (pid.HasValue)
        {
            return new Success(pid.Value);
        }

        return new Failure($"No process found listening on port {port}");
    }

    private static async Task<int?> TryFindWithLsofAsync(
        Port port,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Use lsof command: lsof -ti :PORT
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "lsof",
                    Arguments = $"-ti :{port.Value}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var pidString = output.Trim().Split('\n').FirstOrDefault();
                if (int.TryParse(pidString, out var pid))
                {
                    return pid;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "lsof command failed for port {Port} (may not be installed)", port.Value);
            return null;
        }
    }

    private static async Task<int?> TryFindWithSsAsync(
        Port port,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Use ss command: ss -tlnp sport = :PORT
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ss",
                    Arguments = $"-tlnp sport = :{port.Value}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                // Parse ss output: users:(("processName",pid=12345,fd=3))
                // Example: LISTEN 0   1   0.0.0.0:8080   0.0.0.0:*   users:(("python3",pid=24940,fd=3))
                var match = PidRegex().Match(output);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var pid))
                {
                    return pid;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ss command failed for port {Port}", port.Value);
            return null;
        }
    }

    [GeneratedRegex(@"pid=(\d+)", RegexOptions.Compiled)]
    private static partial Regex PidRegex();
}
```

### Step 3: Refactor `WindowsProcessFinder` to static class

Replace `src/PerformanceTester.Infrastructure/ProcessFinding/WindowsProcessFinder.cs` with:

```csharp
using System.Diagnostics;
using JoanComasFdz.Result;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;
using static JoanComasFdz.Result.Result<int, string>;

namespace PerformanceTester.Infrastructure.ProcessFinding;

/// <summary>
/// Windows-specific process finder using PowerShell Get-NetTCPConnection.
/// </summary>
internal static class WindowsProcessFinder
{
    /// <summary>
    /// Finds the process ID listening on the specified port using PowerShell.
    /// </summary>
    public static async Task<Result<int, string>> FindProcessOnPortAsync(
        Port port,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Use PowerShell: Get-NetTCPConnection -LocalPort {port} | Select-Object -ExpandProperty OwningProcess
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"Get-NetTCPConnection -LocalPort {port.Value} -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                if (int.TryParse(output.Trim(), out var pid))
                {
                    return new Success(pid);
                }
            }

            return new Failure($"No process found listening on port {port}");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "PowerShell command failed for port {Port}", port.Value);
            return new Failure($"PowerShell command failed for port {port}: {ex.Message}");
        }
    }
}
```

### Step 4: Delete `IProcessFinder.cs`

Delete file: `src/PerformanceTester.Infrastructure/ProcessFinding/IProcessFinder.cs`

### Step 5: Commit

This won't build yet (ServiceDiscovery and ServiceCollectionExtensions still reference the old types). Commit the delegate and finders now; the next task wires everything together.

```bash
git add src/PerformanceTester.Infrastructure/ProcessFinding/FindProcessOnPort.cs src/PerformanceTester.Infrastructure/ProcessFinding/LinuxProcessFinder.cs src/PerformanceTester.Infrastructure/ProcessFinding/WindowsProcessFinder.cs && git rm src/PerformanceTester.Infrastructure/ProcessFinding/IProcessFinder.cs && git commit -m "refactor: replace IProcessFinder interface with FindProcessOnPort delegate

Apply functional pattern to process finders (Guidelines 1, 2, 12):
- Named delegate replaces internal single-operation interface
- LinuxProcessFinder and WindowsProcessFinder become static classes
- Logger passed as explicit parameter, captured in closure at DI registration

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>"
```

---

## Task 5: Refactor ServiceDiscovery to Static Class with Named Delegate

This task extracts the core polling logic into a static `ServiceDiscovery` class that accepts `Port` (not `int`), replaces the `IServiceDiscovery` interface with a `FindServiceProcessId` named delegate (single method, single implementation — Guideline 12), updates the DI composition to wire the delegate directly via closure (no adapter class needed), and eliminates the invalid-port scenario entirely via the Port value object.

**Files:**

- Delete: `src/PerformanceTester.Infrastructure/IServiceDiscovery.cs`
- Create: `src/PerformanceTester.Infrastructure/FindServiceProcessId.cs` (named delegate)
- Modify: `src/PerformanceTester.Infrastructure/ProcessFinding/ServiceDiscovery.cs` → static class, accepts `Port`
- Modify: `src/PerformanceTester.Infrastructure/ServiceCollectionExtensions.cs`
- Modify: `src/PerformanceTester.Orchestration/Phases/SetupPhaseDependencies.cs` (resolve delegate, pass `Port` directly)

### Step 1: Delete `IServiceDiscovery.cs` and create `FindServiceProcessId.cs` named delegate

Delete file: `src/PerformanceTester.Infrastructure/IServiceDiscovery.cs`

Create file `src/PerformanceTester.Infrastructure/FindServiceProcessId.cs`:

```csharp
using JoanComasFdz.Result;
using PerformanceTester.Infrastructure.ValueObjects;

namespace PerformanceTester.Infrastructure;

/// <summary>
/// Finds the process ID listening on the specified port.
/// Named delegate replacing IServiceDiscovery interface (Guideline 12).
/// </summary>
/// <param name="port">The validated port to check.</param>
/// <param name="timeout">Maximum time to wait for service to appear.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>
/// Success with process ID if found, or Failure with reason if not found within timeout.
/// </returns>
public delegate Task<Result<int, string>> FindServiceProcessId(
    Port port,
    TimeSpan timeout,
    CancellationToken cancellationToken = default);
```

### Step 2: Rewrite `ServiceDiscovery.cs` as a static class accepting `Port`

Replace `src/PerformanceTester.Infrastructure/ProcessFinding/ServiceDiscovery.cs` with:

```csharp
using System.Net.NetworkInformation;
using JoanComasFdz.Result;
using Microsoft.Extensions.Logging;
using PerformanceTester.Infrastructure.ValueObjects;
using static JoanComasFdz.Result.Result<int, string>;

namespace PerformanceTester.Infrastructure.ProcessFinding;

/// <summary>
/// Service discovery logic for finding processes listening on network ports.
/// Pure static class with explicit parameters (Guidelines 1, 2).
/// Accepts <see cref="Port"/> value object — port range is guaranteed valid (Guideline 22).
/// </summary>
internal static class ServiceDiscovery
{
    /// <summary>
    /// Polls for a process listening on the specified port until found or timeout.
    /// </summary>
    public static async Task<Result<int, string>> FindServiceProcessIdAsync(
        Port port,
        TimeSpan timeout,
        FindProcessOnPort findProcessOnPort,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Searching for service on port {Port} (timeout: {Timeout}s)",
            port.Value,
            timeout.TotalSeconds);

        var startTime = DateTime.UtcNow;
        var lastLogTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check if port is listening before attempting to find process
            if (IsPortListening(port))
            {
                var result = await findProcessOnPort(port, cancellationToken);
                if (result.IsSuccess)
                {
                    logger.LogInformation(
                        "Found service on port {Port}: PID {ProcessId}",
                        port.Value,
                        result.SuccessValue);
                    return result;
                }
            }

            // Log progress every 5 seconds
            if ((DateTime.UtcNow - lastLogTime).TotalSeconds >= 5)
            {
                var elapsed = DateTime.UtcNow - startTime;
                logger.LogDebug(
                    "Still searching for service on port {Port} (elapsed: {Elapsed}s)",
                    port.Value,
                    (int)elapsed.TotalSeconds);
                lastLogTime = DateTime.UtcNow;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        logger.LogWarning(
            "Service not found on port {Port} after {Timeout}s",
            port.Value,
            timeout.TotalSeconds);
        return new Failure($"No service found on port {port} within {timeout}");
    }

    private static bool IsPortListening(Port port)
    {
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        var listeners = properties.GetActiveTcpListeners();
        return listeners.Any(l => l.Port == port.Value);
    }
}
```

### Step 3: Update `ServiceCollectionExtensions.cs` — wire delegate directly via closure

Replace `src/PerformanceTester.Infrastructure/ServiceCollectionExtensions.cs` with:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.Common;
using PerformanceTester.Infrastructure.Database;
using PerformanceTester.Infrastructure.ProcessFinding;
using PerformanceTester.Infrastructure.RabbitMQ;

namespace PerformanceTester.Infrastructure;

/// <summary>
/// Extension methods for registering Infrastructure services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Infrastructure services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="postgresConnectionString">PostgreSQL connection string.</param>
    /// <param name="rabbitMqConnectionString">RabbitMQ AMQP connection string (format: amqp://user:password@host:port).</param>
    /// <param name="rabbitMqManagementPort">Optional RabbitMQ Management API port. If not specified, infers from AMQP port.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string postgresConnectionString,
        string rabbitMqConnectionString,
        int? rabbitMqManagementPort = null)
    {
        // Platform detection determines which static finder to use (Guideline 14: delegates for internal wiring)
        var platformResult = OSPlatformDetector.GetCurrentPlatform();

        // Unsupported platform is a deployment error — the app cannot function without a process finder
        if (platformResult.IsFailure)
        {
            throw new PlatformNotSupportedException(platformResult.FailureError);
        }

        var platform = platformResult.SuccessValue;

        // No adapter class — the closure IS the implementation (Guideline 12)
        services.AddSingleton<FindServiceProcessId>(sp =>
        {
            var discoveryLogger = sp.GetRequiredService<ILogger<ServiceDiscovery>>();

            // Dunet Match — exhaustive at compile time, no UnreachableException needed
            var finderLogger = platform.Match(
                linux: _ => (ILogger)sp.GetRequiredService<ILogger<LinuxProcessFinder>>(),
                windows: _ => (ILogger)sp.GetRequiredService<ILogger<WindowsProcessFinder>>());

            FindProcessOnPort findProcessOnPort = platform.Match(
                linux: _ => (FindProcessOnPort)((p, ct) => LinuxProcessFinder.FindProcessOnPortAsync(p, finderLogger, ct)),
                windows: _ => (FindProcessOnPort)((p, ct) => WindowsProcessFinder.FindProcessOnPortAsync(p, finderLogger, ct)));

            return (port, timeout, ct) => ServiceDiscovery.FindServiceProcessIdAsync(
                port,
                timeout,
                findProcessOnPort,
                discoveryLogger,
                ct);
        });

        // DatabaseCleaner receives connection string and logger
        services.AddSingleton<IDatabase>(sp => new DatabaseCleaner(
            postgresConnectionString,
            sp.GetRequiredService<ILogger<DatabaseCleaner>>()));

        // RabbitMqCleaner receives connection string, logger, and optional management port
        services.AddSingleton<IRabbitMQ>(sp => new RabbitMqCleaner(
                rabbitMqConnectionString,
                sp.GetRequiredService<ILogger<RabbitMqCleaner>>(),
                rabbitMqManagementPort));

        return services;
    }
}
```

### Step 4: Update `SetupPhaseDependencies.cs` — resolve delegate, pass `Port` directly

In `src/PerformanceTester.Orchestration/Phases/SetupPhaseDependencies.cs`, change:

```csharp
var serviceDiscovery = services.GetRequiredService<IServiceDiscovery>();
```

to:

```csharp
var discoverService = services.GetRequiredService<FindServiceProcessId>();
```

And change the lambda from:

```csharp
findServiceProcessId: () => serviceDiscovery.FindServiceProcessIdAsync(config.ServicePort.Value, TimeSpan.FromSeconds(30), ct),
```

to:

```csharp
findServiceProcessId: () => discoverService(config.ServicePort, TimeSpan.FromSeconds(30), ct),
```

Note: resolves the `FindServiceProcessId` delegate directly from DI. Passes `config.ServicePort` (the `Port` VO) instead of unwrapping `.Value`. The delegate is invoked directly — no method name needed.

### Step 5: Build to verify compilation

Run:

```bash
cd /workspace/performance-tester-dotnet && dotnet build src/PerformanceTester.Infrastructure && dotnet build src/PerformanceTester.Orchestration
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` for both.

### Step 6: Run ServiceDiscovery tests (expect failures)

Run:

```bash
cd /workspace/performance-tester-dotnet && dotnet test src/PerformanceTester.Infrastructure.IntegrationTests --filter "FullyQualifiedName~ServiceDiscoveryTests" --logger "console;verbosity=detailed"
```

Expected: Build **fails** because the tests still reference `IServiceDiscovery` which no longer exists. We fix the tests in the next task.

### Step 7: Commit

```bash
git rm src/PerformanceTester.Infrastructure/IServiceDiscovery.cs && git add src/PerformanceTester.Infrastructure/FindServiceProcessId.cs src/PerformanceTester.Infrastructure/ProcessFinding/ServiceDiscovery.cs src/PerformanceTester.Infrastructure/ServiceCollectionExtensions.cs src/PerformanceTester.Orchestration/Phases/SetupPhaseDependencies.cs && git commit -m "refactor: replace IServiceDiscovery with FindServiceProcessId delegate

Apply functional pattern to ServiceDiscovery (Guidelines 1, 2, 12, 22):
- Core logic is a static method with explicit parameters
- IServiceDiscovery interface replaced by FindServiceProcessId delegate
- Named delegate accepts Port VO instead of int (parse don't validate)
- Port VO guarantees validity, no inline validation needed
- Unsupported platform throws at startup (deployment error)
- DI closure wires static core directly — no adapter class needed

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>"
```

---

## Task 6: Update Tests for Port-Based API

This task updates the ServiceDiscovery tests and assertion helpers to use `FindServiceProcessId` delegate with `Port` instead of `IServiceDiscovery` with `int`, removes the invalid-port test (the `Port` value object makes invalid states unrepresentable), and updates the test infrastructure to resolve the delegate from DI.

**Files:**

- Modify: `src/PerformanceTester.Infrastructure.IntegrationTests/Infrastructure/Infrastructure.cs` (resolve delegate)
- Modify: `src/PerformanceTester.Infrastructure.IntegrationTests/Infrastructure/InfrastructureAssertions.cs`
- Modify: `src/PerformanceTester.Infrastructure.IntegrationTests/ServiceDiscoveryTests.cs`

### Step 1: Update test infrastructure to resolve `FindServiceProcessId` delegate

In `src/PerformanceTester.Infrastructure.IntegrationTests/Infrastructure/Infrastructure.cs`:

Change the property type and resolution:

```csharp
// Before:
public IServiceDiscovery ServiceDiscovery { get; private set; } = null!;
// ...
ServiceDiscovery = _host.Services.GetRequiredService<IServiceDiscovery>();

// After:
public FindServiceProcessId FindServiceProcessId { get; private set; } = null!;
// ...
FindServiceProcessId = _host.Services.GetRequiredService<FindServiceProcessId>();
```

### Step 2: Update assertion methods in `InfrastructureAssertions.cs`

In `src/PerformanceTester.Infrastructure.IntegrationTests/Infrastructure/InfrastructureAssertions.cs`:

1. **Delete** the `ThrowsArgumentOutOfRangeForInvalidPort` method entirely — `Port` VO makes invalid states unrepresentable.

2. **Update** `FindsCurrentProcessOnPort` to use `FindServiceProcessId` delegate with `Port`:

```csharp
    /// <summary>
    /// Asserts that the delegate finds the current process ID on the specified port.
    /// </summary>
    public static async Task FindsCurrentProcessOnPort(
        this AssertingThat<FindServiceProcessId> assertingThat,
        Port port,
        TimeSpan timeout)
    {
        var result = await assertingThat.InstanceToAssert(port, timeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(Environment.ProcessId, result.SuccessValue);
    }
```

3. **Update** `FindsNoProcessOnPort` to use `FindServiceProcessId` delegate with `Port`:

```csharp
    /// <summary>
    /// Asserts that the delegate returns a failure when no service is running on the port.
    /// </summary>
    public static async Task FindsNoProcessOnPort(
        this AssertingThat<FindServiceProcessId> assertingThat,
        Port port,
        TimeSpan timeout)
    {
        var result = await assertingThat.InstanceToAssert(port, timeout);
        Assert.True(result.IsFailure);
    }
```

4. **Add** the required `using` at the top of the file:

```csharp
using PerformanceTester.Infrastructure.ValueObjects;
```

### Step 2: Update tests in `ServiceDiscoveryTests.cs`

Replace `src/PerformanceTester.Infrastructure.IntegrationTests/ServiceDiscoveryTests.cs` with:

```csharp
using JoanComasFdz.AssertingThat;
using PerformanceTester.Infrastructure.IntegrationTests.Infrastructure;
using PerformanceTester.Infrastructure.ValueObjects;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.Infrastructure.IntegrationTests;

public sealed class ServiceDiscoveryTests(ITestOutputHelper output) : IntegrationTest(output)
{
    [Fact]
    public async Task FindServiceProcessIdAsync_WhenServiceIsRunning_ShouldReturnProcessId()
    {
        // Arrange - Start a TCP listener on an available port
        var testPortNumber = System.OS.StartProcessOnPort(); // OS assigns available port
        var testPort = Port.FromInt(testPortNumber);

        try
        {
            // Act - Should find the current process (listener runs in this test process)
            await Asserting.That(System.Infrastructure.FindServiceProcessId)
                .FindsCurrentProcessOnPort(testPort, TimeSpan.FromSeconds(5));
        }
        finally
        {
            // Cleanup
            System.OS.StopProcessOnPort(testPortNumber);
        }
    }

    [Fact]
    public async Task FindServiceProcessIdAsync_WhenNoServiceOnPort_ShouldReturnFailure()
    {
        // Arrange - port 54321 should be unused
        var unusedPort = Port.FromInt(54321);

        // Act & Assert
        await Asserting.That(System.Infrastructure.FindServiceProcessId)
            .FindsNoProcessOnPort(unusedPort, TimeSpan.FromSeconds(2));
    }
}
```

Note: The invalid-port test is removed. Port validation is the responsibility of `Port.Create()` which is tested at the parsing boundary (CLI). The `FindServiceProcessId` delegate can't receive an invalid port.

### Step 3: Run ServiceDiscovery tests

Run:

```bash
cd /workspace/performance-tester-dotnet && dotnet test src/PerformanceTester.Infrastructure.IntegrationTests --filter "FullyQualifiedName~ServiceDiscoveryTests" --logger "console;verbosity=detailed"
```

Expected: Both tests pass:

- `FindServiceProcessIdAsync_WhenServiceIsRunning_ShouldReturnProcessId` — PASS
- `FindServiceProcessIdAsync_WhenNoServiceOnPort_ShouldReturnFailure` — PASS

### Step 4: Commit

```bash
git add src/PerformanceTester.Infrastructure.IntegrationTests/Infrastructure/Infrastructure.cs src/PerformanceTester.Infrastructure.IntegrationTests/Infrastructure/InfrastructureAssertions.cs src/PerformanceTester.Infrastructure.IntegrationTests/ServiceDiscoveryTests.cs && git commit -m "test: update ServiceDiscovery tests for FindServiceProcessId delegate

Tests resolve FindServiceProcessId delegate instead of IServiceDiscovery
interface. Assertions use Port VO (parse don't validate, Guideline 22).
The invalid-port test is removed — Port VO makes invalid states
unrepresentable at the delegate level.

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>"
```

---

## Task 7: Final Verification

**Files:** None (read-only)

### Step 1: Run full Infrastructure test suite

Run:

```bash
cd /workspace/performance-tester-dotnet && dotnet test src/PerformanceTester.Infrastructure.IntegrationTests --logger "console;verbosity=detailed"
```

Expected: All Infrastructure tests pass (ServiceDiscovery + Database + RabbitMQ).

### Step 2: Build entire solution to verify no downstream breakage

Run:

```bash
cd /workspace/performance-tester-dotnet && dotnet build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

### Step 3: Run full solution test suite

Run:

```bash
cd /workspace/performance-tester-dotnet && dotnet test
```

Expected: All tests across all projects pass.

### Step 4: Verify the architecture matches the target

After refactoring, the file structure should be:

```
src/PerformanceTester.Common/
└── OSPlatformDetector.cs              ← Returns Result<SupportedPlatform, string> (no exception)

src/PerformanceTester.Infrastructure/
├── ValueObjects/
│   └── Port.cs                        ← Moved from Orchestration (infrastructure concern)
├── ProcessFinding/
│   ├── FindProcessOnPort.cs           ← Named delegate (replaces IProcessFinder)
│   ├── LinuxProcessFinder.cs          ← Static class, explicit params
│   ├── WindowsProcessFinder.cs        ← Static class, explicit params
│   ├── ServiceDiscovery.cs            ← Static class, accepts Port VO
├── FindServiceProcessId.cs            ← Named delegate (replaces IServiceDiscovery interface)
└── ServiceCollectionExtensions.cs      ← Composition root (delegate wiring via Dunet Match)
```

The dependency flow:

```
FindServiceProcessId (named delegate — accepts Port)
  └── ServiceDiscovery.FindServiceProcessIdAsync (static method)
              ├── extracts port.Value for IsPortListening and logging
              └── FindProcessOnPort (named delegate, accepts Port)
                    ├── LinuxProcessFinder.FindProcessOnPortAsync (static, extracts .Value for shell commands)
                    └── WindowsProcessFinder.FindProcessOnPortAsync (static, extracts .Value for shell commands)
```

Composed at: `ServiceCollectionExtensions.AddInfrastructure()` (Dunet `Match` for exhaustive platform binding, closure captures logger and finder delegate).

---

## Architecture Before vs After

### Before (Traditional OOP + Exceptions)

```
OSPlatformDetector.GetCurrentPlatform()  → throws PlatformNotSupportedException

IServiceDiscovery ──→ ServiceDiscovery (sealed class, single implementation)
                          ├── _processFinder: IProcessFinder (stored field)
                          │     ├── LinuxProcessFinder (sealed class, stored _logger)
                          │     └── WindowsProcessFinder (sealed class, stored _logger)
                          ├── _logger: ILogger<ServiceDiscovery> (stored field)
                          └── FindServiceProcessIdAsync(int port) → throws ArgumentOutOfRangeException
```

- 1 public interface (`IServiceDiscovery`, DI boundary)
- 1 internal interface (`IProcessFinder`)
- 3 sealed classes with constructor injection
- 4 stored fields across classes
- 2 exception types for known scenarios
- Duplicate port validation (Port VO in Orchestration + inline check in ServiceDiscovery)
- Platform binding via DI container (`AddSingleton<IProcessFinder, LinuxProcessFinder>()`)
- `SupportedPlatform` enum requires default arms with `UnreachableException` (not exhaustive)

### After (Functional + Value Objects + Result Types)

```
OSPlatformDetector.GetCurrentPlatform()  → returns Result<SupportedPlatform, string>

Port (value object, moved to Infrastructure) — guarantees valid port range at parse time

FindServiceProcessId (named delegate, registered in DI — accepts Port)
  └── closure wires to: ServiceDiscovery.FindServiceProcessIdAsync(Port port) (static method)
                              ├── Port VO guarantees validity — no inline validation
                              ├── findProcessOnPort: FindProcessOnPort (delegate param, accepts Port)
                              │     ├── LinuxProcessFinder.FindProcessOnPortAsync (static, extracts .Value for shell commands)
                              │     └── WindowsProcessFinder.FindProcessOnPortAsync (static, extracts .Value for shell commands)
                              └── logger: ILogger (explicit param, captured in closure)
```

- 0 public interfaces (replaced by `FindServiceProcessId` named delegate)
- 0 internal interfaces (replaced by `FindProcessOnPort` named delegate)
- 2 static classes (process finders + core logic)
- 0 adapter classes (DI closure wires directly)
- 0 exceptions for known runtime scenarios (Results for process-not-found; startup throws for unsupported platform)
- 0 duplicate validation (Port VO is single source of truth)
- Platform binding via Dunet `Match` + closure in `ServiceCollectionExtensions` (exhaustive at compile time)
