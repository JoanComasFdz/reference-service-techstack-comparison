# Refactor Reporting to Accept Value Objects Instead of Primitive Strings

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Move `ResultsOutputFolder`, `ResultsSourceFolder`, and their base types (`FolderPath`, `ExistingFolderPath`) from `PerformanceTester.Orchestration` into `PerformanceTester.Reporting`, then change all 4 Reporting public APIs to accept value objects instead of `string` paths — eliminating redundant `IsNullOrWhiteSpace` guards and `.Value` unwrapping at every call site.

**Architecture:** Reporting *owns* the concept of report folders — it writes to them (`ReportGenerator`, `ChartGenerator`) and reads from them (`TestReportLoader`, `ComparisonReportGenerator`). Moving the folder value objects into Reporting follows VSA ownership. Orchestration already depends on Reporting, so it gets the types transitively. CLI depends on both, so it also gets them.

**Tech Stack:** C# / .NET 9, `JoanComasFdz.Result` monadic Result type, existing value object patterns

---

## Design Decisions

### Why move into Reporting rather than a shared ValueObjects project?

VSA principle: the project that owns the concept owns the types. Reporting owns report folders. A shared project would violate "no central Core project" and create unnecessary coupling. The other value objects (`Port`, `EventCount`, `ApiDuration`, etc.) remain in Orchestration because they relate to test orchestration, not reporting.

### Why not keep folder value objects in Orchestration and have Reporting depend on it?

That would create a circular dependency. Orchestration already depends on Reporting. Reporting must not depend on Orchestration.

### Why does Reporting need a reference to JoanComasFdz.Result?

The `Create()` methods on value objects return `Result<T, string>`. Reporting already has a dependency on JoanComasFdz.Result's package... actually it doesn't. Let's check: Reporting needs the `Result` type only if it exposes `Create()` methods. Since `Create()` is called by CLI (at the ingress boundary), the Result type must be visible to CLI. Reporting already depends on no result library, and CLI already depends on `JoanComasFdz.Result`. So: Reporting needs a project reference to `JoanComasFdz.Result` to define the value objects.

### Why add `NonEmptyString` base reference?

`FolderPath` inherits from `NonEmptyString`, and `NonEmptyString` is already in Orchestration. Rather than duplicating, we move `NonEmptyString` into a location both projects can reach. Since Orchestration depends on Reporting, `NonEmptyString` must stay in Orchestration or move somewhere upstream. **Solution:** Keep `NonEmptyString` in Orchestration. Make `FolderPath` NOT inherit from `NonEmptyString` — instead, inline the `Create<T>` pattern with its own base. This is acceptable because `FolderPath` and `ExistingFolderPath` already have their own `Create<T>` implementations that differ from `NonEmptyString.Create<T>` only in error message. This avoids any dependency changes for `NonEmptyString`.

Actually, looking at the current code: `FolderPath` inherits `NonEmptyString` which provides `Value`, `ToString()`, and `Create<T>()`. `ExistingFolderPath` is its own standalone hierarchy with its own `Value`, `ToString()`, and `Create<T>()`. To move both into Reporting without dragging `NonEmptyString`, we give `FolderPath` its own `Value`/`ToString()`/`Create<T>()` — identical to what it inherits today but self-contained.

### What about `DatabaseName` that also inherits `NonEmptyString` via `FolderPath`'s namespace?

`DatabaseName` inherits `NonEmptyString` directly and stays in Orchestration. No change needed. Only `FolderPath` (and its child `ResultsOutputFolder`) move.

### What changes in the API signatures?

| Method | Before | After |
|--------|--------|-------|
| `ReportGenerator.GenerateReportAsync` | `string outputDirectory` | `ResultsOutputFolder outputFolder` |
| `ChartGenerator.GenerateChartAsync` | `string outputPath` | `ResultsOutputFolder outputFolder`, `TestReport testReport` (chart path derived internally) |
| `ComparisonReportGenerator.GenerateComparisonReportAsync` | `string outputPath` | `ResultsSourceFolder sourceFolder`, `IEnumerable<TestReport> testReports` |
| `TestReportLoader.LoadFromFolderAsync` | `string folderPath` | `ResultsSourceFolder sourceFolder` |

### Why change ChartGenerator to derive the chart path internally?

The chart filename follows a fixed convention: `test-report-{timestamp}-{servicename}.chart.png`. Currently the Orchestrator manually constructs this path using `Path.Combine(config.ResultsFolder.Value, ...)`. This is reporting's concern — the filename convention is defined by Reporting. Moving path construction into `ChartGenerator` makes it self-contained and eliminates another `.Value` unwrap in the Orchestrator.

### Why change ComparisonReportGenerator to derive the output path internally?

Same reasoning — the summary filename convention `test-report-{timestamp}-summary.md` is a Reporting concern. Moving it into `ComparisonReportGenerator` removes path construction from `CompareCommand`.

---

## Tasks

### Task 1: Move folder value objects into Reporting

Move 4 files from `Orchestration/ValueObjects/` into `Reporting/ValueObjects/`, making `FolderPath` self-contained (no `NonEmptyString` dependency).

**Files:**
- Create: `src/PerformanceTester.Reporting/ValueObjects/FolderPath.cs`
- Create: `src/PerformanceTester.Reporting/ValueObjects/ExistingFolderPath.cs`
- Create: `src/PerformanceTester.Reporting/ValueObjects/ResultsOutputFolder.cs`
- Create: `src/PerformanceTester.Reporting/ValueObjects/ResultsSourceFolder.cs`
- Delete: `src/PerformanceTester.Orchestration/ValueObjects/FolderPath.cs`
- Delete: `src/PerformanceTester.Orchestration/ValueObjects/ExistingFolderPath.cs`
- Delete: `src/PerformanceTester.Orchestration/ValueObjects/ResultsOutputFolder.cs`
- Delete: `src/PerformanceTester.Orchestration/ValueObjects/ResultsSourceFolder.cs`
- Modify: `src/PerformanceTester.Reporting/PerformanceTester.Reporting.csproj` (add JoanComasFdz.Result reference)

**Step 1: Add project reference to Reporting.csproj**

Add `<ProjectReference Include="..\JoanComasFdz.Result\JoanComasFdz.Result.csproj" />` to `PerformanceTester.Reporting.csproj`.

**Step 2: Create `FolderPath.cs` in Reporting (self-contained, no NonEmptyString dependency)**

```csharp
using JoanComasFdz.Result;

namespace PerformanceTester.Reporting.ValueObjects;

public record FolderPath
{
    public string Value { get; }
    protected FolderPath(string value) => Value = value;

    public override string ToString() => Value;

    protected static Result<T, string> Create<T>(string value, string displayName, Func<string, T> factory)
        where T : FolderPath =>
        !string.IsNullOrWhiteSpace(value)
            ? new Result<T, string>.Success(factory(value.Trim()))
            : new Result<T, string>.Failure($"{displayName} cannot be empty");
}
```

**Step 3: Create `ExistingFolderPath.cs` in Reporting (unchanged logic)**

```csharp
using JoanComasFdz.Result;

namespace PerformanceTester.Reporting.ValueObjects;

public record ExistingFolderPath
{
    public string Value { get; }
    protected ExistingFolderPath(string value) => Value = value;

    public override string ToString() => Value;

    protected static Result<T, string> Create<T>(string value, string displayName, Func<string, T> factory)
        where T : ExistingFolderPath
    {
        if (string.IsNullOrWhiteSpace(value))
            return new Result<T, string>.Failure($"{displayName} cannot be empty");

        var trimmed = value.Trim();

        if (!Directory.Exists(trimmed))
            return new Result<T, string>.Failure($"{displayName} not found: {trimmed}");

        return new Result<T, string>.Success(factory(trimmed));
    }
}
```

**Step 4: Create `ResultsOutputFolder.cs` in Reporting**

```csharp
using JoanComasFdz.Result;

namespace PerformanceTester.Reporting.ValueObjects;

public sealed record ResultsOutputFolder : FolderPath
{
    private ResultsOutputFolder(string value) : base(value) { }

    public static Result<ResultsOutputFolder, string> Create(string value) =>
        Create(value, "Results folder", v => new ResultsOutputFolder(v));

    public static ResultsOutputFolder FromString(string value) => new(value);
}
```

**Step 5: Create `ResultsSourceFolder.cs` in Reporting**

```csharp
using JoanComasFdz.Result;

namespace PerformanceTester.Reporting.ValueObjects;

public sealed record ResultsSourceFolder : ExistingFolderPath
{
    private ResultsSourceFolder(string value) : base(value) { }

    public static Result<ResultsSourceFolder, string> Create(string value) =>
        Create(value, "Results folder", v => new ResultsSourceFolder(v));
}
```

**Step 6: Delete the 4 files from Orchestration/ValueObjects/**

Delete:
- `src/PerformanceTester.Orchestration/ValueObjects/FolderPath.cs`
- `src/PerformanceTester.Orchestration/ValueObjects/ExistingFolderPath.cs`
- `src/PerformanceTester.Orchestration/ValueObjects/ResultsOutputFolder.cs`
- `src/PerformanceTester.Orchestration/ValueObjects/ResultsSourceFolder.cs`

**Step 7: Update namespace imports**

Fix all `using PerformanceTester.Orchestration.ValueObjects` references that use `ResultsOutputFolder`, `ResultsSourceFolder`, `FolderPath`, or `ExistingFolderPath` to `using PerformanceTester.Reporting.ValueObjects`:

- `src/PerformanceTester.Orchestration/TestConfiguration.cs` — add `using PerformanceTester.Reporting.ValueObjects;`
- `src/PerformanceTester.Cli/Commands/TestCommand.cs` — add `using PerformanceTester.Reporting.ValueObjects;`
- `src/PerformanceTester.Cli/Commands/CompareCommand.cs` — change from `using PerformanceTester.Orchestration.ValueObjects;` to `using PerformanceTester.Reporting.ValueObjects;`

Note: `TestConfiguration.cs` already has `using PerformanceTester.Orchestration.ValueObjects;` for the other value objects. It now needs BOTH usings since `ResultsOutputFolder` moved to Reporting.

**Step 8: Build and verify**

Run: `dotnet build src/PerformanceTester.sln`
Expected: Build succeeds with zero warnings. All existing tests still pass without modification.

**Step 9: Commit**

```bash
git add -A
git commit -m "refactor: move folder value objects from Orchestration to Reporting"
```

---

### Task 2: Change `ReportGenerator` to accept `ResultsOutputFolder`

**Files:**
- Modify: `src/PerformanceTester.Reporting/ReportGeneration/ReportGenerator.cs`

**Step 1: Update `GenerateReportAsync` signature and remove string guard**

Change the first parameter from `string outputDirectory` to `ResultsOutputFolder outputFolder`. Remove the `IsNullOrWhiteSpace` guard (the value object guarantees non-empty). Replace all internal usages of `outputDirectory` with `outputFolder.Value`.

Before:
```csharp
public async Task GenerateReportAsync(
    string outputDirectory,
    TestReport testReport,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(outputDirectory))
    {
        throw new ArgumentException("Output directory cannot be null or empty.", nameof(outputDirectory));
    }
```

After:
```csharp
public async Task GenerateReportAsync(
    ResultsOutputFolder outputFolder,
    TestReport testReport,
    CancellationToken cancellationToken = default)
{
```

Also update the `outputDirectory` variable throughout the method body — rename to `outputFolder.Value` where path operations are needed, or extract `var outputDirectory = outputFolder.Value;` at the top to minimize churn on the 7 private `Generate*` calls that pass `outputDirectory` as their first parameter.

Recommended approach: keep the private methods accepting `string outputDirectory` (they're `private static` implementation details), and just unwrap once at the top of the public method:

```csharp
public async Task GenerateReportAsync(
    ResultsOutputFolder outputFolder,
    TestReport testReport,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(testReport);

    var outputDirectory = outputFolder.Value;

    // Ensure output directory exists
    Directory.CreateDirectory(outputDirectory);
    // ... rest unchanged
```

**Step 2: Update callers**

In `TestOrchestrator.cs` (line 573-576), change:
```csharp
await _reportGenerator.GenerateReportAsync(
    config.ResultsFolder.Value,
    testReport,
    cancellationToken);
```
To:
```csharp
await _reportGenerator.GenerateReportAsync(
    config.ResultsFolder,
    testReport,
    cancellationToken);
```

In test files (`ReportGeneratorTests.cs`, contract tests), change all calls from:
```csharp
await System.Reporting.ReportGenerator.GenerateReportAsync(outputDirectory, testReport);
```
To:
```csharp
await System.Reporting.ReportGenerator.GenerateReportAsync(
    ResultsOutputFolder.FromString(outputDirectory), testReport);
```

Add `using PerformanceTester.Reporting.ValueObjects;` to each test file.

**Step 3: Build and run tests**

Run: `dotnet test src/PerformanceTester.sln`
Expected: All tests pass.

**Step 4: Commit**

```bash
git add -A
git commit -m "refactor: ReportGenerator accepts ResultsOutputFolder instead of string"
```

---

### Task 3: Change `TestReportLoader` to accept `ResultsSourceFolder`

**Files:**
- Modify: `src/PerformanceTester.Reporting/ReportGeneration/TestReportLoader.cs`

**Step 1: Update `LoadFromFolderAsync` signature**

Change from `string folderPath` to `ResultsSourceFolder sourceFolder`. Extract `var folderPath = sourceFolder.Value;` at the top.

Before:
```csharp
public static async Task<IReadOnlyList<TestReport>> LoadFromFolderAsync(
    string folderPath,
    CancellationToken cancellationToken = default)
{
    var reportFiles = Directory.GetFiles(folderPath, "test-report-*.json")
```

After:
```csharp
public static async Task<IReadOnlyList<TestReport>> LoadFromFolderAsync(
    ResultsSourceFolder sourceFolder,
    CancellationToken cancellationToken = default)
{
    var folderPath = sourceFolder.Value;

    var reportFiles = Directory.GetFiles(folderPath, "test-report-*.json")
```

**Step 2: Update caller in `CompareCommand.cs`**

Change (line 64-65):
```csharp
var testReports = await TestReportLoader.LoadFromFolderAsync(
    folder.Value, cancellationToken);
```
To:
```csharp
var testReports = await TestReportLoader.LoadFromFolderAsync(
    folder, cancellationToken);
```

**Step 3: Update test files**

In `TestReportLoaderTests.cs`, wrap `outputDirectory` strings:
```csharp
var loaded = await TestReportLoader.LoadFromFolderAsync(
    ResultsSourceFolder.Create(outputDirectory).SuccessValue);
```

Note: `ResultsSourceFolder.Create()` validates that the directory exists. Since tests create temp directories via `System.FileSystem.CreateTempDirectory()`, the directories will exist and `Create()` will succeed. Use `.SuccessValue` to unwrap.

Alternatively, add a `FromString` factory to `ResultsSourceFolder` for test convenience (bypasses existence check):

```csharp
public static ResultsSourceFolder FromString(string value) => new(value);
```

Then tests use: `ResultsSourceFolder.FromString(outputDirectory)`.

Add the `FromString` factory to `ResultsSourceFolder` — it follows the pattern already established by `ResultsOutputFolder.FromString`.

**Step 4: Build and run tests**

Run: `dotnet test src/PerformanceTester.sln`
Expected: All tests pass.

**Step 5: Commit**

```bash
git add -A
git commit -m "refactor: TestReportLoader accepts ResultsSourceFolder instead of string"
```

---

### Task 4: Change `ChartGenerator` to accept `ResultsOutputFolder`

The chart path currently gets manually constructed by `TestOrchestrator` using `Path.Combine(config.ResultsFolder.Value, ...)`. Move this into `ChartGenerator` since the filename convention is Reporting's concern.

**Files:**
- Modify: `src/PerformanceTester.Reporting/ChartGeneration/ChartGenerator.cs`

**Step 1: Update `GenerateChartAsync` signature**

Change from `string outputPath` to `ResultsOutputFolder outputFolder`. Derive the chart path internally from `TestReport` data.

Before:
```csharp
public static async Task GenerateChartAsync(
    string outputPath,
    TestReport testReport,
    ILogger logger,
    CancellationToken cancellationToken = default)
{
    var config = ChartConfig.Default;

    ArgumentException.ThrowIfNullOrWhiteSpace(outputPath, nameof(outputPath));
    ArgumentNullException.ThrowIfNull(testReport, nameof(testReport));

    var outputDirectory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
    {
        Directory.CreateDirectory(outputDirectory);
    }

    var basePath = outputPath.Replace(".chart.png", "");
```

After:
```csharp
public static async Task GenerateChartAsync(
    ResultsOutputFolder outputFolder,
    TestReport testReport,
    ILogger logger,
    CancellationToken cancellationToken = default)
{
    var config = ChartConfig.Default;

    ArgumentNullException.ThrowIfNull(testReport, nameof(testReport));

    Directory.CreateDirectory(outputFolder.Value);

    var serviceName = testReport.MonitoredProcess?.Name?.ToLowerInvariant() ?? "unknown";
    var timestamp = testReport.TestDate.ToString("yyyyMMdd_HHmmss");
    var basePath = Path.Combine(outputFolder.Value, $"test-report-{timestamp}-{serviceName}");
    var outputPath = $"{basePath}.chart.png";
```

**Step 2: Update caller in `TestOrchestrator.cs`**

Remove the manual chart path construction (lines 581-584) and simplify:

Before:
```csharp
// Step 7: Generate chart
var serviceName = processMetrics.FirstOrDefault()?.ProcessName ?? "unknown";
var chartPath = Path.Combine(
    config.ResultsFolder.Value,
    $"test-report-{testReport.TestDate:yyyyMMdd_HHmmss}-{serviceName.ToLowerInvariant()}.chart.png");

_logger.LogInformation("Generating chart to {Path}", chartPath);

await ChartGenerator.GenerateChartAsync(
    chartPath,
    testReport,
    _logger,
    cancellationToken: cancellationToken);
```

After:
```csharp
// Step 7: Generate chart
_logger.LogInformation("Generating chart to {Folder}", config.ResultsFolder);

await ChartGenerator.GenerateChartAsync(
    config.ResultsFolder,
    testReport,
    _logger,
    cancellationToken: cancellationToken);
```

**Step 3: Update test files**

In `ChartGeneratorTests.cs`, each test creates a `testDir` via `System.FileSystem.CreateTempDirectory()`, then uses a `TestReportFileBuilder` to write supplementary files and get the chart output path. The test needs to change from passing `outputPath` (string) to `ResultsOutputFolder.FromString(testDir)`.

Before:
```csharp
await ChartGenerator.GenerateChartAsync(outputPath, testReport, System.Reporting.Logger);
```

After:
```csharp
await ChartGenerator.GenerateChartAsync(
    ResultsOutputFolder.FromString(testDir), testReport, System.Reporting.Logger);
```

Note: The tests currently use `TestReportFileBuilder` to get the outputPath. After this change, the chart path is derived internally by `ChartGenerator`, so tests need to construct the expected chart path from convention to verify the file was created. Review each test to ensure assertions on the output file still work. The `TestReportFileBuilder` writes supplementary JSON files — those still go to `testDir`. The chart PNG path follows `test-report-{timestamp}-{servicename}.chart.png` inside `testDir`.

In `ReportGeneratorTests.cs`, there is one test (around line 480-487) that calls `ChartGenerator.GenerateChartAsync`. Update it similarly.

**Step 4: Return the chart path from `GenerateChartAsync`**

To allow callers (like the Orchestrator) to log the actual path, change the return type from `Task` to `Task<string>` returning the computed `outputPath`:

```csharp
public static async Task<string> GenerateChartAsync(
    ResultsOutputFolder outputFolder,
    TestReport testReport,
    ILogger logger,
    CancellationToken cancellationToken = default)
{
    // ... existing logic ...
    return outputPath;
}
```

Then in `TestOrchestrator.cs`:
```csharp
var chartPath = await ChartGenerator.GenerateChartAsync(
    config.ResultsFolder,
    testReport,
    _logger,
    cancellationToken: cancellationToken);

_logger.LogInformation("Metrics chart saved to: {Path}", chartPath);
```

**Step 5: Build and run tests**

Run: `dotnet test src/PerformanceTester.sln`
Expected: All tests pass.

**Step 6: Commit**

```bash
git add -A
git commit -m "refactor: ChartGenerator accepts ResultsOutputFolder and derives chart path internally"
```

---

### Task 5: Change `ComparisonReportGenerator` to accept `ResultsSourceFolder`

Move the summary filename convention into `ComparisonReportGenerator`.

**Files:**
- Modify: `src/PerformanceTester.Reporting/ComparisonGeneration/ComparisonReportGenerator.cs`

**Step 1: Update `GenerateComparisonReportAsync` signature**

Change from `string outputPath` to `ResultsSourceFolder sourceFolder` (the source folder is where reports are read from AND where the summary is written — it's the same folder).

Before:
```csharp
public async Task GenerateComparisonReportAsync(
    string outputPath,
    IEnumerable<TestReport> testReports,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(outputPath))
    {
        throw new ArgumentException("Output path cannot be null or empty.", nameof(outputPath));
    }
```

After:
```csharp
public async Task<string> GenerateComparisonReportAsync(
    ResultsSourceFolder sourceFolder,
    IEnumerable<TestReport> testReports,
    CancellationToken cancellationToken = default)
{
```

Derive the output path internally:
```csharp
    var reports = testReports?.ToList() ?? throw new ArgumentNullException(nameof(testReports));

    if (reports.Count == 0)
    {
        throw new ArgumentException("Test reports collection cannot be empty.", nameof(testReports));
    }

    var latestTestDate = reports.Max(r => r.TestDate);
    var timestamp = latestTestDate.ToString("yyyyMMdd_HHmmss");
    var outputPath = Path.Combine(sourceFolder.Value, $"test-report-{timestamp}-summary.md");
```

Return the computed path:
```csharp
    await File.WriteAllTextAsync(outputPath, markdown.ToString(), cancellationToken);
    return outputPath;
```

**Step 2: Update caller in `CompareCommand.cs`**

Before:
```csharp
var latestTestDate = testReports.Max(r => r.TestDate);
var timestamp = latestTestDate.ToString("yyyyMMdd_HHmmss");
var outputPath = Path.Combine(folder.Value, $"test-report-{timestamp}-summary.md");

await comparisonGenerator.GenerateComparisonReportAsync(outputPath, testReports, cancellationToken);
consoleWriter.WriteSuccess($"Comparison report saved to: {outputPath}");
```

After:
```csharp
var outputPath = await comparisonGenerator.GenerateComparisonReportAsync(
    folder, testReports, cancellationToken);
consoleWriter.WriteSuccess($"Comparison report saved to: {outputPath}");
```

**Step 3: Update test files**

In `ComparisonReportGeneratorTests.cs`, tests create `outputPath` via `System.FileSystem.CreateTempFilePath()` and pass it directly. After the change, tests need to provide a `ResultsSourceFolder`. Since tests use temp directories, create the temp dir and wrap it:

Before:
```csharp
var outputPath = System.FileSystem.CreateTempFilePath("comparison-report-test", ".md");
await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(outputPath, reports);
```

After:
```csharp
var tempDir = System.FileSystem.CreateTempDirectory("comparison-report-test");
var sourceFolder = ResultsSourceFolder.FromString(tempDir);
var outputPath = await System.Reporting.ComparisonReportGenerator.GenerateComparisonReportAsync(
    sourceFolder, reports);
```

Update assertions that check the output file to use the returned `outputPath`.

Similarly update `ComparisonReportContractTests.cs`.

**Step 4: Build and run tests**

Run: `dotnet test src/PerformanceTester.sln`
Expected: All tests pass.

**Step 5: Commit**

```bash
git add -A
git commit -m "refactor: ComparisonReportGenerator accepts ResultsSourceFolder and derives output path internally"
```

---

### Task 6: Remove duplicate `Directory.CreateDirectory` calls from callers

Now that `ReportGenerator` and `ChartGenerator` handle directory creation internally, remove the duplicate calls from `TestOrchestrator` and `TestCommand`.

**Files:**
- Modify: `src/PerformanceTester.Orchestration/TestOrchestrator.cs`
- Modify: `src/PerformanceTester.Cli/Commands/TestCommand.cs`

**Step 1: Remove from TestOrchestrator.cs**

Delete line 571: `Directory.CreateDirectory(config.ResultsFolder.Value);`

(ReportGenerator.GenerateReportAsync already calls `Directory.CreateDirectory`.)

**Step 2: Remove from TestCommand.cs**

Delete line 164: `Directory.CreateDirectory(config.ResultsFolder.Value);`

(The orchestrator flows into ReportGenerator which creates the directory.)

**Step 3: Build and run tests**

Run: `dotnet test src/PerformanceTester.sln`
Expected: All tests pass.

**Step 4: Commit**

```bash
git add -A
git commit -m "refactor: remove duplicate Directory.CreateDirectory calls from callers"
```

---

### Task 7: Run full build and test suite

**Step 1: Build entire solution**

Run: `dotnet build src/PerformanceTester.sln`
Expected: Build succeeds with zero warnings.

**Step 2: Run full test suite**

Run: `dotnet test src/PerformanceTester.sln`
Expected: All tests pass.

---

## Summary of Changes

| File | Change | Why |
|------|--------|-----|
| `Reporting/ValueObjects/FolderPath.cs` (new) | Self-contained base record with `Value`/`Create<T>` | Owns folder path concept in Reporting |
| `Reporting/ValueObjects/ExistingFolderPath.cs` (new) | Base record with existence validation | Owns existing folder concept in Reporting |
| `Reporting/ValueObjects/ResultsOutputFolder.cs` (moved) | Namespace change only | Reporting owns report output folders |
| `Reporting/ValueObjects/ResultsSourceFolder.cs` (moved) | Namespace change, add `FromString` | Reporting owns report source folders |
| `Orchestration/ValueObjects/FolderPath.cs` (deleted) | Moved to Reporting | — |
| `Orchestration/ValueObjects/ExistingFolderPath.cs` (deleted) | Moved to Reporting | — |
| `Orchestration/ValueObjects/ResultsOutputFolder.cs` (deleted) | Moved to Reporting | — |
| `Orchestration/ValueObjects/ResultsSourceFolder.cs` (deleted) | Moved to Reporting | — |
| `ReportGenerator.cs` | `string` → `ResultsOutputFolder`, remove guard | Value object guarantees non-empty |
| `ChartGenerator.cs` | `string` → `ResultsOutputFolder`, derive path internally | Reporting owns filename conventions |
| `ComparisonReportGenerator.cs` | `string` → `ResultsSourceFolder`, derive path internally | Reporting owns filename conventions |
| `TestReportLoader.cs` | `string` → `ResultsSourceFolder` | Value object guarantees existing folder |
| `TestOrchestrator.cs` | Remove `.Value` unwraps, remove chart path construction | Value objects flow through |
| `CompareCommand.cs` | Remove `.Value` unwraps, remove output path construction | Value objects flow through |
| `TestCommand.cs` | Remove `Directory.CreateDirectory` | Reporting handles directory creation |
| `PerformanceTester.Reporting.csproj` | Add JoanComasFdz.Result reference | Value objects use Result type |
| Test files (many) | Wrap string paths in value objects | Match new API signatures |

**Guards eliminated:** 3 (`IsNullOrWhiteSpace` in ReportGenerator, ChartGenerator, ComparisonReportGenerator)
**`.Value` unwraps eliminated:** ~6 (across TestOrchestrator and CompareCommand)
**Path construction moved:** 2 (chart path, summary path moved into Reporting)
**Risk:** Low — value objects preserve identical runtime behavior; only the API boundary changes.
