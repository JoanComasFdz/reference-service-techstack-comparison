# Upgrade Performance Tester from .NET 9 to .NET 10 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Upgrade all 24 projects in the performance-tester-dotnet solution from .NET 9.0 to .NET 10.0, including all NuGet package updates and infrastructure/script references.

**Architecture:** The solution has 24 projects (12 production + 10 test + 1 netstandard2.0 + 1 solution file). All net9.0 projects become net10.0. The `PerformanceTester.Common` project stays on `netstandard2.0` (it's framework-agnostic by design). NuGet packages are updated to their latest .NET 10-compatible versions. Scripts, Dockerfile, and mise config are updated to reference .NET 10.

**Tech Stack:** .NET 10.0 SDK (10.0.102+), updated Microsoft.Extensions.* 10.0.x packages, updated Serilog/Npgsql/xUnit ecosystem packages

---

## Package Version Reference

The following NuGet packages will be updated. This table serves as a reference for all tasks below.

| Package | Current Version | Target Version | Notes |
|---------|----------------|----------------|-------|
| **Microsoft.Extensions.Hosting** | 9.0.0 | 10.0.2 | |
| **Microsoft.Extensions.Hosting.Abstractions** | 9.0.0 | 10.0.2 | |
| **Microsoft.Extensions.DependencyInjection** | 9.0.0 | 10.0.2 | |
| **Microsoft.Extensions.DependencyInjection.Abstractions** | 9.0.0 | 10.0.2 | |
| **Microsoft.Extensions.Logging** | 9.0.0 | 10.0.2 | |
| **Microsoft.Extensions.Logging.Abstractions** | 9.0.0 | 10.0.2 | |
| **Microsoft.Extensions.Configuration.Json** | 9.0.0 | 10.0.2 | |
| **Microsoft.Extensions.Configuration.EnvironmentVariables** | 9.0.0 | 10.0.2 | |
| **System.Management** | 9.0.0 | 10.0.2 | Windows-only, in Reporting |
| **Npgsql** | 9.0.3 | 10.0.1 | |
| **RabbitMQ.Client** | 7.0.0 | 7.2.0 | |
| **Serilog** | 4.2.0 | 4.3.0 | |
| **Serilog.Extensions.Hosting** | 9.0.0 | 10.0.0 | |
| **Serilog.Settings.Configuration** | 9.0.0 | 10.0.0 | |
| **Serilog.Sinks.Console** | 6.0.0 | 6.1.1 | |
| **Serilog.Sinks.File** | 6.0.0 | 7.0.0 | |
| **Serilog.Enrichers.Thread** | 4.0.0 | 4.0.0 | No update needed |
| **Serilog.Enrichers.Environment** | 3.0.1 | 3.0.1 | No update needed |
| **Polly** | 8.0.0 | 8.6.5 | |
| **Docker.DotNet** | 3.125.15 | 3.125.15 | No update needed |
| **CloudNative.CloudEvents** | 2.8.0 | 2.8.0 | No update needed |
| **CloudNative.CloudEvents.SystemTextJson** | 2.8.0 | 2.8.0 | No update needed |
| **ScottPlot** | 5.1.57 | 5.1.57 | No update needed |
| **MathNet.Numerics** | 5.0.0 | 5.0.0 | No update needed |
| **System.CommandLine** | 2.0.0-beta4.22272.1 | 2.0.2 | Now stable! |
| **System.CommandLine.Hosting** | 0.4.0-alpha.22272.1 | 0.4.0-alpha.25306.1 | Still prerelease |
| **xunit** | 2.9.3 | 2.9.3 | No update needed |
| **xunit.runner.visualstudio** | 2.8.2 / 3.0.2 | 3.1.5 | Standardize all to latest |
| **xunit.abstractions** | 2.0.3 | 2.0.3 | No update needed |
| **Microsoft.NET.Test.Sdk** | 17.8.0 / 17.12.0 | 18.0.1 | Standardize all to latest |
| **coverlet.collector** | 6.0.0 / 6.0.2 | 6.0.4 | Standardize all to latest |
| **Testcontainers.PostgreSql** | 4.1.0 | 4.10.0 | |
| **Testcontainers.RabbitMq** | 4.1.0 | 4.10.0 | |
| **FluentAssertions** | 7.0.0 | 8.8.0 | |
| **JoanComasFdz.AssertingThat** | 1.0.0 | 1.0.0 | No update needed |

---

### Task 1: Update TargetFramework in all .csproj files

**Files:**
- Modify: All 23 `.csproj` files under `performance-tester-dotnet/src/` that target `net9.0`
- Skip: `PerformanceTester.Common/PerformanceTester.Common.csproj` (stays `netstandard2.0`)

**Step 1: Replace TargetFramework in all .csproj files**

Use a find-and-replace across all `.csproj` files to change:
```xml
<TargetFramework>net9.0</TargetFramework>
```
to:
```xml
<TargetFramework>net10.0</TargetFramework>
```

The following 23 files need this change:
1. `PerformanceTester.Infrastructure/PerformanceTester.Infrastructure.csproj`
2. `PerformanceTester.Infrastructure.IntegrationTests/PerformanceTester.Infrastructure.IntegrationTests.csproj`
3. `PerformanceTester.EventPublishing/PerformanceTester.EventPublishing.csproj`
4. `PerformanceTester.EventPublishing.IntegrationTests/PerformanceTester.EventPublishing.IntegrationTests.csproj`
5. `PerformanceTester.EventConsuming/PerformanceTester.EventConsuming.csproj`
6. `PerformanceTester.EventConsuming.IntegrationTests/PerformanceTester.EventConsuming.IntegrationTests.csproj`
7. `PerformanceTester.ProcessMonitoring/PerformanceTester.ProcessMonitoring.csproj`
8. `PerformanceTester.ProcessMonitoring.IntegrationTests/PerformanceTester.ProcessMonitoring.IntegrationTests.csproj`
9. `PerformanceTester.DockerMonitoring/PerformanceTester.DockerMonitoring.csproj`
10. `PerformanceTester.DockerMonitoring.IntegrationTests/PerformanceTester.DockerMonitoring.IntegrationTests.csproj`
11. `PerformanceTester.ApiLoadTesting/PerformanceTester.ApiLoadTesting.csproj`
12. `PerformanceTester.ApiLoadTesting.IntegrationTests/PerformanceTester.ApiLoadTesting.IntegrationTests.csproj`
13. `PerformanceTester.Reporting/PerformanceTester.Reporting.csproj`
14. `PerformanceTester.Reporting.IntegrationTests/PerformanceTester.Reporting.IntegrationTests.csproj`
15. `PerformanceTester.SystemMonitoring/PerformanceTester.SystemMonitoring.csproj`
16. `PerformanceTester.SystemMonitoring.IntegrationTests/PerformanceTester.SystemMonitoring.IntegrationTests.csproj`
17. `PerformanceTester.SystemMonitoring.Tests/PerformanceTester.SystemMonitoring.Tests.csproj`
18. `PerformanceTester.Orchestration/PerformanceTester.Orchestration.csproj`
19. `PerformanceTester.Orchestration.IntegrationTests/PerformanceTester.Orchestration.IntegrationTests.csproj`
20. `PerformanceTester.IntegrationTesting/PerformanceTester.IntegrationTesting.csproj`
21. `PerformanceTester.IntegrationTesting.Tests/PerformanceTester.IntegrationTesting.Tests.csproj`
22. `PerformanceTester.Cli/PerformanceTester.Cli.csproj`
23. `PerformanceTester.Cli.Tests/PerformanceTester.Cli.Tests.csproj`

**Step 2: Verify Common stays on netstandard2.0**

Confirm `PerformanceTester.Common/PerformanceTester.Common.csproj` still has:
```xml
<TargetFramework>netstandard2.0</TargetFramework>
```

**Step 3: Commit**

```bash
cd /workspace/performance-tester-dotnet
git add -A src/
git commit -m "chore: update TargetFramework from net9.0 to net10.0 in all projects"
```

---

### Task 2: Update NuGet packages in production projects

**Files:**
- Modify: `PerformanceTester.Infrastructure/PerformanceTester.Infrastructure.csproj`
- Modify: `PerformanceTester.EventPublishing/PerformanceTester.EventPublishing.csproj`
- Modify: `PerformanceTester.EventConsuming/PerformanceTester.EventConsuming.csproj`
- Modify: `PerformanceTester.ProcessMonitoring/PerformanceTester.ProcessMonitoring.csproj`
- Modify: `PerformanceTester.DockerMonitoring/PerformanceTester.DockerMonitoring.csproj`
- Modify: `PerformanceTester.ApiLoadTesting/PerformanceTester.ApiLoadTesting.csproj`
- Modify: `PerformanceTester.Reporting/PerformanceTester.Reporting.csproj`
- Modify: `PerformanceTester.SystemMonitoring/PerformanceTester.SystemMonitoring.csproj`
- Modify: `PerformanceTester.Orchestration/PerformanceTester.Orchestration.csproj`
- Modify: `PerformanceTester.IntegrationTesting/PerformanceTester.IntegrationTesting.csproj`
- Modify: `PerformanceTester.Cli/PerformanceTester.Cli.csproj`

**Step 1: Update each production .csproj file**

Apply the following version changes per project (use the Package Version Reference table above). Key changes per project:

**PerformanceTester.Infrastructure.csproj:**
- `Npgsql` 9.0.3 -> 10.0.1
- `RabbitMQ.Client` 7.0.0 -> 7.2.0
- `Microsoft.Extensions.DependencyInjection.Abstractions` 9.0.0 -> 10.0.2
- `Microsoft.Extensions.Logging.Abstractions` 9.0.0 -> 10.0.2

**PerformanceTester.EventPublishing.csproj:**
- `RabbitMQ.Client` 7.0.0 -> 7.2.0
- `Polly` 8.0.0 -> 8.6.5
- `Microsoft.Extensions.DependencyInjection.Abstractions` 9.0.0 -> 10.0.2
- `Microsoft.Extensions.Logging.Abstractions` 9.0.0 -> 10.0.2

**PerformanceTester.EventConsuming.csproj:**
- `RabbitMQ.Client` 7.0.0 -> 7.2.0
- `Microsoft.Extensions.DependencyInjection.Abstractions` 9.0.0 -> 10.0.2
- `Microsoft.Extensions.Hosting.Abstractions` 9.0.0 -> 10.0.2
- `Microsoft.Extensions.Logging.Abstractions` 9.0.0 -> 10.0.2

**PerformanceTester.ProcessMonitoring.csproj:**
- `Microsoft.Extensions.DependencyInjection.Abstractions` 9.0.0 -> 10.0.2
- `Microsoft.Extensions.Hosting.Abstractions` 9.0.0 -> 10.0.2
- `Microsoft.Extensions.Logging.Abstractions` 9.0.0 -> 10.0.2

**PerformanceTester.DockerMonitoring.csproj:**
- `Microsoft.Extensions.Hosting.Abstractions` 9.0.0 -> 10.0.2
- `Microsoft.Extensions.Logging.Abstractions` 9.0.0 -> 10.0.2

**PerformanceTester.ApiLoadTesting.csproj:**
- `Microsoft.Extensions.DependencyInjection.Abstractions` 9.0.0 -> 10.0.2
- `Microsoft.Extensions.Logging.Abstractions` 9.0.0 -> 10.0.2

**PerformanceTester.Reporting.csproj:**
- `System.Management` 9.0.0 -> 10.0.2

**PerformanceTester.SystemMonitoring.csproj:**
- `Microsoft.Extensions.DependencyInjection.Abstractions` 9.0.0 -> 10.0.2
- `Microsoft.Extensions.Hosting.Abstractions` 9.0.0 -> 10.0.2
- `Microsoft.Extensions.Logging.Abstractions` 9.0.0 -> 10.0.2

**PerformanceTester.Orchestration.csproj:**
- `Serilog` 4.2.0 -> 4.3.0
- `Microsoft.Extensions.Hosting` 9.0.0 -> 10.0.2

**PerformanceTester.IntegrationTesting.csproj:**
- `Microsoft.Extensions.Logging` 9.0.0 -> 10.0.2
- `Microsoft.Extensions.Logging.Abstractions` 9.0.0 -> 10.0.2
- `Npgsql` 9.0.3 -> 10.0.1
- `RabbitMQ.Client` 7.0.0 -> 7.2.0
- `Testcontainers.PostgreSql` 4.1.0 -> 4.10.0
- `Testcontainers.RabbitMq` 4.1.0 -> 4.10.0

**PerformanceTester.Cli.csproj:**
- `System.CommandLine` 2.0.0-beta4.22272.1 -> 2.0.2
- `System.CommandLine.Hosting` 0.4.0-alpha.22272.1 -> 0.4.0-alpha.25306.1
- `Serilog` 4.2.0 -> 4.3.0
- `Serilog.Extensions.Hosting` 9.0.0 -> 10.0.0
- `Serilog.Settings.Configuration` 9.0.0 -> 10.0.0
- `Serilog.Sinks.Console` 6.0.0 -> 6.1.1
- `Serilog.Sinks.File` 6.0.0 -> 7.0.0
- `Microsoft.Extensions.Configuration.Json` 9.0.0 -> 10.0.2
- `Microsoft.Extensions.Configuration.EnvironmentVariables` 9.0.0 -> 10.0.2

**Step 2: Commit**

```bash
cd /workspace/performance-tester-dotnet
git add -A src/
git commit -m "chore: update NuGet packages in production projects for .NET 10"
```

---

### Task 3: Update NuGet packages in test projects

**Files:**
- Modify: `PerformanceTester.Infrastructure.IntegrationTests/PerformanceTester.Infrastructure.IntegrationTests.csproj`
- Modify: `PerformanceTester.EventPublishing.IntegrationTests/PerformanceTester.EventPublishing.IntegrationTests.csproj`
- Modify: `PerformanceTester.EventConsuming.IntegrationTests/PerformanceTester.EventConsuming.IntegrationTests.csproj`
- Modify: `PerformanceTester.ProcessMonitoring.IntegrationTests/PerformanceTester.ProcessMonitoring.IntegrationTests.csproj`
- Modify: `PerformanceTester.DockerMonitoring.IntegrationTests/PerformanceTester.DockerMonitoring.IntegrationTests.csproj`
- Modify: `PerformanceTester.ApiLoadTesting.IntegrationTests/PerformanceTester.ApiLoadTesting.IntegrationTests.csproj`
- Modify: `PerformanceTester.Reporting.IntegrationTests/PerformanceTester.Reporting.IntegrationTests.csproj`
- Modify: `PerformanceTester.SystemMonitoring.IntegrationTests/PerformanceTester.SystemMonitoring.IntegrationTests.csproj`
- Modify: `PerformanceTester.SystemMonitoring.Tests/PerformanceTester.SystemMonitoring.Tests.csproj`
- Modify: `PerformanceTester.Orchestration.IntegrationTests/PerformanceTester.Orchestration.IntegrationTests.csproj`
- Modify: `PerformanceTester.IntegrationTesting.Tests/PerformanceTester.IntegrationTesting.Tests.csproj`
- Modify: `PerformanceTester.Cli.Tests/PerformanceTester.Cli.Tests.csproj`

**Step 1: Update each test .csproj file**

Standardize all test projects to the same latest versions:

Common updates across ALL test projects:
- `Microsoft.NET.Test.Sdk` -> 18.0.1 (standardize from 17.8.0/17.12.0)
- `coverlet.collector` -> 6.0.4 (standardize from 6.0.0/6.0.2)
- `xunit.runner.visualstudio` -> 3.1.5 (standardize from 2.8.2/3.0.2)

Per-project specific updates:

**Infrastructure.IntegrationTests:**
- `Microsoft.Extensions.Hosting` 9.0.0 -> 10.0.2

**EventPublishing.IntegrationTests:**
- `Microsoft.Extensions.Hosting` 9.0.0 -> 10.0.2

**EventConsuming.IntegrationTests:**
- `Microsoft.Extensions.Hosting` 9.0.0 -> 10.0.2

**ProcessMonitoring.IntegrationTests:**
- `Microsoft.Extensions.Hosting` 9.0.0 -> 10.0.2

**DockerMonitoring.IntegrationTests:**
- `Microsoft.Extensions.Hosting` 9.0.0 -> 10.0.2

**ApiLoadTesting.IntegrationTests:**
(No Microsoft.Extensions updates beyond the common ones)

**Reporting.IntegrationTests:**
- `Microsoft.Extensions.Hosting` 9.0.0 -> 10.0.2

**SystemMonitoring.IntegrationTests:**
(No Microsoft.Extensions updates beyond the common ones)

**Orchestration.IntegrationTests:**
- `Microsoft.Extensions.Hosting` 9.0.0 -> 10.0.2

**IntegrationTesting.Tests:**
- `Npgsql` 9.0.3 -> 10.0.1
- `RabbitMQ.Client` 7.0.0 -> 7.2.0
- `Microsoft.Extensions.DependencyInjection` 9.0.0 -> 10.0.2

**Cli.Tests:**
- `FluentAssertions` 7.0.0 -> 8.8.0

**Step 2: Commit**

```bash
cd /workspace/performance-tester-dotnet
git add -A src/
git commit -m "chore: update NuGet packages in test projects for .NET 10"
```

---

### Task 4: Update mise configuration

**Files:**
- Modify: `/workspace/.mise.toml`

**Step 1: Update dotnet version in .mise.toml**

Change line 20:
```toml
dotnet = "9.0.306"
```
to:
```toml
dotnet = "10.0.102"
```

**Step 2: Commit**

```bash
cd /workspace
git add .mise.toml
git commit -m "chore: update mise dotnet version from 9.0.306 to 10.0.102"
```

---

### Task 5: Update Devcontainer Dockerfile

**Files:**
- Modify: `/workspace/.devcontainer/Dockerfile`

**Step 1: Update .NET SDK installation**

Change lines 52-58:
```dockerfile
# Install .NET 9 SDK
RUN wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
  dpkg -i packages-microsoft-prod.deb && \
  rm packages-microsoft-prod.deb && \
  apt-get update && \
  apt-get install -y dotnet-sdk-9.0 && \
  apt-get clean && rm -rf /var/lib/apt/lists/*
```
to:
```dockerfile
# Install .NET 10 SDK
RUN wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
  dpkg -i packages-microsoft-prod.deb && \
  rm packages-microsoft-prod.deb && \
  apt-get update && \
  apt-get install -y dotnet-sdk-10.0 && \
  apt-get clean && rm -rf /var/lib/apt/lists/*
```

**Step 2: Commit**

```bash
cd /workspace
git add .devcontainer/Dockerfile
git commit -m "chore: update Dockerfile from dotnet-sdk-9.0 to dotnet-sdk-10.0"
```

---

### Task 6: Update shell scripts (setup, verify, build, test)

**Files:**
- Modify: `/workspace/scripts/infrastructure/setup/setup-environment.sh`
- Modify: `/workspace/scripts/infrastructure/setup/verify-environment.sh`
- Modify: `/workspace/scripts/tools/build/test-all-builds.sh`
- Modify: `/workspace/scripts/tools/testing/run-all-tests.sh`
- Modify: `/workspace/scripts/tools/build/clean-binaries.sh`

**Step 1: Update setup-environment.sh**

Change all references from `.NET 9` to `.NET 10`. Specifically:
- Line 240: Change `"Tools to install: Maven, Go, Rust, .NET 9, Bun, Python 3.13"` to `"Tools to install: Maven, Go, Rust, .NET 10, Bun, Python 3.13"`
- Lines 260-282: Update the devcontainer detection logic to check for `.NET 10.x` instead of `.NET 9.x`:
  - Change `9.*` pattern matching to `10.*`

**Step 2: Update verify-environment.sh**

- Line 317: Change `"Checking .NET 9..."` to `"Checking .NET 10..."`
- Line 319: Update the `check_tool_version` call from `"9.0"` to `"10.0"`
- Line 323: Update the version check from `9.*` to `10.*`
- Line 325: Update the success message to reference `10.x`
- Line 327: Update the warning message to reference `10.x`
- Line 330: Change `"Install with: mise install dotnet@9.0.306"` to `"Install with: mise install dotnet@10.0.102"`

**Step 3: Update test-all-builds.sh**

- Lines 224-238: Change `"Test .NET 9"`, `"Testing .NET 9..."`, and `".NET 9 (JIT)"` to `.NET 10` equivalents
- Lines 241-256: Change `"Test .NET 9 AOT"`, `"Testing .NET 9 AOT..."`, and `".NET 9 AOT"` to `.NET 10` equivalents

**Step 4: Update run-all-tests.sh**

- Line 33: Change `bin/Release/net9.0/performance-tester` to `bin/Release/net10.0/performance-tester`
- Lines 105-106: Change `".NET 9 (port 8092)"` and `".NET 9 AOT (port 8093)"` to `.NET 10`
- Lines 592-603: Change all `.NET 9` references and `bin/Release/net9.0/dotnet9ReferenceService.dll` to `net10.0`
- Lines 608-619: Change all `.NET 9 AOT` references and `bin/Release/net9.0/linux-x64/publish/dotnet9AotReferenceService` to `net10.0`

**Note about the `dotnet9ReferenceService.dll` and `dotnet9AotReferenceService` binary names:** These are the names of the *implementation* binaries under `/workspace/implementations/dotnet9/` and `/workspace/implementations/dotnet9aot/`. They are NOT part of the performance tester. The binary *paths* contain `net9.0` because those implementations target .NET 9. Do NOT change these paths - they refer to a separate project that is not being upgraded in this plan. Only change paths for the performance tester itself (the `performance-tester` binary).

So in `run-all-tests.sh`:
- Line 33: `bin/Release/net9.0/performance-tester` -> `bin/Release/net10.0/performance-tester` (this IS the performance tester binary)
- Lines 603, 619: Do NOT change `dotnet9ReferenceService.dll` or `dotnet9AotReferenceService` paths - these are the .NET 9 service implementations being tested, not the tester itself

**Step 5: Update clean-binaries.sh**

- Lines 70, 78, 86: These reference `.NET 9 service` and `.NET 9 Events service` and `.NET 9 AOT service` - these refer to the service implementations, NOT the performance tester. Do NOT change these.

**Step 6: Commit**

```bash
cd /workspace
git add scripts/
git commit -m "chore: update shell scripts for .NET 10 SDK references"
```

---

### Task 7: Build the solution and fix any compilation issues

**Step 1: Restore packages**

Run:
```bash
cd /workspace/performance-tester-dotnet/src
dotnet restore
```
Expected: All packages restore successfully with the new versions.

**Step 2: Build the solution**

Run:
```bash
cd /workspace/performance-tester-dotnet/src
dotnet build -c Release
```
Expected: Clean build with 0 errors. Some warnings may appear.

**Step 3: Fix any compilation errors**

If there are breaking changes from package updates (particularly `System.CommandLine` 2.0.2 which graduated from beta, `FluentAssertions` 8.x which has API changes, or `Serilog.Sinks.File` 7.0.0), fix them.

Known potential breaking changes:
- **System.CommandLine 2.0.2** (was beta): API may have changed from the beta. Check `PerformanceTester.Cli` for any compilation errors and fix accordingly.
- **FluentAssertions 8.x** (was 7.0.0): Major version bump. Check `PerformanceTester.Cli.Tests` for assertion API changes.
- **Serilog.Sinks.File 7.0.0** (was 6.0.0): Major version bump. Check Serilog configuration in `PerformanceTester.Cli`.
- **Npgsql 10.0.1** (was 9.0.3): Major version bump. Check for any API changes in `PerformanceTester.Infrastructure` and `PerformanceTester.IntegrationTesting`.

**Step 4: Fix any warnings**

Address any new warnings introduced by the upgrade, following the project's convention of `TreatWarningsAsErrors` in most projects.

**Step 5: Commit fixes (if any)**

```bash
cd /workspace/performance-tester-dotnet
git add -A src/
git commit -m "fix: resolve compilation issues from .NET 10 package upgrades"
```

---

### Task 8: Run unit tests

**Step 1: Run all unit tests**

Run:
```bash
cd /workspace/performance-tester-dotnet/src
dotnet test PerformanceTester.Cli.Tests/ --no-build -c Release -v normal
dotnet test PerformanceTester.SystemMonitoring.Tests/ --no-build -c Release -v normal
dotnet test PerformanceTester.IntegrationTesting.Tests/ --no-build -c Release -v normal
```

Expected: All tests pass.

**Step 2: Fix any test failures**

If tests fail due to API changes in updated packages (especially FluentAssertions 8.x), fix the test code.

**Step 3: Commit fixes (if any)**

```bash
cd /workspace/performance-tester-dotnet
git add -A src/
git commit -m "fix: update tests for .NET 10 compatibility"
```

---

### Task 9: Update documentation references

**Files:**
- Modify: `/workspace/performance-tester-dotnet/README.md` - change `.NET 9.0 SDK` to `.NET 10.0 SDK`
- Modify: `/workspace/performance-tester-dotnet/CLAUDE.md` - change all `.NET 9` references to `.NET 10` where they refer to the performance tester (not the service implementations)
- Modify: `/workspace/CLAUDE.md` - update the performance tester `.NET 9 SDK` reference if present

**Step 1: Update README.md**

Change `.NET 9.0 SDK` requirement to `.NET 10.0 SDK`.

**Step 2: Update performance-tester-dotnet/CLAUDE.md**

Change references to `.NET 9` that describe the performance tester itself:
- Line 7: `"specialized .NET 9 subsystem"` -> `"specialized .NET 10 subsystem"`
- Line 21: `.NET 9.0 SDK` -> `.NET 10.0 SDK`
- Line 867: `.NET 9.0 SDK (minimum version: 9.0.0)` -> `.NET 10.0 SDK (minimum version: 10.0.0)`
- Line 1040: `Modern .NET 9 patterns` -> `Modern .NET 10 patterns`
- Line 1066: `Target Framework: .NET 9.0` -> `Target Framework: .NET 10.0`

Do NOT change references to the dotnet9/dotnet9aot service implementations - those are separate projects that remain on .NET 9.

**Step 3: Commit**

```bash
cd /workspace/performance-tester-dotnet
git add README.md CLAUDE.md
cd /workspace
git add CLAUDE.md
git commit -m "docs: update documentation for .NET 10 upgrade"
```
