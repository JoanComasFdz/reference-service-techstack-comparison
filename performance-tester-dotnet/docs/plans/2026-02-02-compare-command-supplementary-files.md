# Compare Command: Load Supplementary Files Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Branch:** `fix/compare-command-supplementary-files`

**Goal:** Fix the .NET `compare` command to load supplementary JSON files (events-throughput, api-throughput, resource-metrics, system-metrics) like Python does, so comparison reports show actual metrics instead of zeros.

**Architecture:** Modify `CompareCommand` to discover and load all related files for each test run, then merge the data into `TestReport` objects before passing to `ComparisonReportGenerator`.

**Tech Stack:** C#, System.Text.Json, existing reporting models

---

## Analysis: Python vs .NET Implementation Differences

### Python Implementation (`compare_test_results.py`)

**File Loading Strategy:**
1. Uses `TestRun` class that holds multiple file contents:
   - `main_report` - from `.json`
   - `resource_metrics` - from `.resource-metrics.json`
   - `api_throughput` - from `.api-throughput.json`
   - `system_metrics` - from `.system-metrics.json`
   - `events_throughput` - from `.events-throughput.json`

2. File discovery uses regex pattern: `test-report-(\d{8}_\d{6})-(.+?)(?:\.json|\.)`
3. Groups files by timestamp to associate main report with supplementary files
4. Reads summary data from supplementary files, e.g.:
   ```python
   def get_events_summary(self) -> dict[str, float]:
       if self.events_throughput:
           return self.events_throughput.get('summary', {})
       return {}
   ```

### .NET Implementation (`CompareCommand.cs`)

**Current Issues:**
1. Only loads main `.json` files (filters out supplementary files on line 87-93)
2. `TestReport` model has sample collections but they're never populated
3. `ComparisonReportGenerator` calculates statistics from empty sample arrays → all zeros

**File Structure (.NET reports):**
```
test-report-20260202_130102-bunreferenceservice.json           # Main report
test-report-20260202_130102-bunreferenceservice.events-throughput.json
test-report-20260202_130102-bunreferenceservice.api-throughput.json
test-report-20260202_130102-bunreferenceservice.resource-metrics.json
test-report-20260202_130102-bunreferenceservice.system-metrics.json
test-report-20260202_130102-bunreferenceservice.postgres-metrics.json
test-report-20260202_130102-bunreferenceservice.rabbitmq-metrics.json
```

---

## Task 1: Create Supplementary File Models for Deserialization

**Files:**
- Create: `src/PerformanceTester.Reporting/ComparisonGeneration/Models/SupplementaryReportModels.cs`

**Step 1: Create models matching JSON structure**

```csharp
namespace PerformanceTester.Reporting.ComparisonGeneration;

/// <summary>
/// Events throughput supplementary file structure.
/// Matches test-report-*.events-throughput.json format.
/// </summary>
public sealed record EventsThroughputReport
{
    public required string TestDate { get; init; }
    public int SamplingIntervalMs { get; init; }
    public IReadOnlyList<EventsThroughputSampleJson> Samples { get; init; } = Array.Empty<EventsThroughputSampleJson>();
    public EventsThroughputSummary? Summary { get; init; }
}

public sealed record EventsThroughputSampleJson
{
    public required string Timestamp { get; init; }
    public double ElapsedSeconds { get; init; }
    public int TotalEvents { get; init; }
    public double EventsPerSecond { get; init; }
}

public sealed record EventsThroughputSummary
{
    public double AvgEventsPerSecond { get; init; }
    public double PeakEventsPerSecond { get; init; }
    public double MinEventsPerSecond { get; init; }
    public double StdDevEventsPerSecond { get; init; }
    public double CvEventsPerSecond { get; init; }
    public double AvgResponseTimeMs { get; init; }
    public int TotalSamples { get; init; }
    public int TotalEvents { get; init; }
}

/// <summary>
/// API throughput supplementary file structure.
/// Matches test-report-*.api-throughput.json format.
/// </summary>
public sealed record ApiThroughputReport
{
    public required string TestDate { get; init; }
    public int SamplingIntervalMs { get; init; }
    public IReadOnlyList<ApiThroughputSampleJson> Samples { get; init; } = Array.Empty<ApiThroughputSampleJson>();
    public ApiThroughputSummary? Summary { get; init; }
}

public sealed record ApiThroughputSampleJson
{
    public required string Timestamp { get; init; }
    public double ElapsedSeconds { get; init; }
    public int TotalCalls { get; init; }
    public double CallsPerSecond { get; init; }
}

public sealed record ApiThroughputSummary
{
    public double AvgCallsPerSecond { get; init; }
    public double PeakCallsPerSecond { get; init; }
    public double MinCallsPerSecond { get; init; }
    public double StdDevCallsPerSecond { get; init; }
    public double CvCallsPerSecond { get; init; }
    public double AvgResponseTimeMs { get; init; }
    public int TotalSamples { get; init; }
    public int TotalCalls { get; init; }
}

/// <summary>
/// Resource metrics supplementary file structure.
/// Matches test-report-*.resource-metrics.json format.
/// </summary>
public sealed record ResourceMetricsFileReport
{
    public required string TestDate { get; init; }
    public ProcessInfoJson? ProcessInfo { get; init; }
    public int SamplingIntervalMs { get; init; }
    public IReadOnlyList<ProcessResourceSampleFileJson> Samples { get; init; } = Array.Empty<ProcessResourceSampleFileJson>();
    public ResourceMetricsSummary? Summary { get; init; }
}

public sealed record ProcessInfoJson
{
    public int Pid { get; init; }
    public required string Name { get; init; }
    public int Port { get; init; }
}

public sealed record ProcessResourceSampleFileJson
{
    public required string Timestamp { get; init; }
    public double CpuPercent { get; init; }
    public double MemoryRssMb { get; init; }
    public int Threads { get; init; }
}

public sealed record ResourceMetricsSummary
{
    public double AvgCpuPercent { get; init; }
    public double PeakCpuPercent { get; init; }
    public double AvgMemoryRssMb { get; init; }
    public double PeakMemoryRssMb { get; init; }
    public int TotalSamples { get; init; }
}

/// <summary>
/// System metrics supplementary file structure.
/// Matches test-report-*.system-metrics.json format.
/// </summary>
public sealed record SystemMetricsFileReport
{
    public required string TestDate { get; init; }
    public int CpuCount { get; init; }
    public int SamplingIntervalMs { get; init; }
    public IReadOnlyList<SystemResourceSampleFileJson> Samples { get; init; } = Array.Empty<SystemResourceSampleFileJson>();
    public SystemMetricsSummary? Summary { get; init; }
    public bool IsWsl2 { get; init; }
}

public sealed record SystemResourceSampleFileJson
{
    public required string Timestamp { get; init; }
    public double CpuPercent { get; init; }
    public double MemoryUsedMb { get; init; }
    public double MemoryTotalMb { get; init; }
    public double MemoryPercent { get; init; }
}

public sealed record SystemMetricsSummary
{
    public double AvgCpuPercent { get; init; }
    public double PeakCpuPercent { get; init; }
    public double MinCpuPercent { get; init; }
    public double AvgMemoryUsedMb { get; init; }
    public double PeakMemoryUsedMb { get; init; }
    public double AvgMemoryPercent { get; init; }
    public double PeakMemoryPercent { get; init; }
    public int TotalSamples { get; init; }
}
```

**Step 2: Verify file compiles**

Run: `dotnet build src/PerformanceTester.Reporting -c Release --verbosity quiet`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/PerformanceTester.Reporting/ComparisonGeneration/Models/SupplementaryReportModels.cs
git commit -m "feat(Reporting): add models for supplementary JSON file deserialization

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 2: Create TestRunLoader to Load All Related Files

**Files:**
- Create: `src/PerformanceTester.Reporting/ComparisonGeneration/TestRunLoader.cs`

**Step 1: Create the loader class**

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using PerformanceTester.Reporting.Shared.Utilities;

namespace PerformanceTester.Reporting.ComparisonGeneration;

/// <summary>
/// Loads test reports with all supplementary files (like Python's TestRun class).
/// Groups files by timestamp and merges data into TestReport objects.
/// </summary>
public static class TestRunLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new Iso8601DateTimeConverter() }
    };

    // Pattern: test-report-{timestamp}-{processname}.{suffix}.json
    private static readonly Regex FilePattern = new(
        @"^test-report-(\d{8}_\d{6})-(.+?)(?:\.(.+))?\.json$",
        RegexOptions.Compiled);

    /// <summary>
    /// Discovers and loads all test reports from a folder, including supplementary files.
    /// </summary>
    public static async Task<List<TestReport>> LoadTestReportsAsync(
        string folderPath,
        Action<string>? onInfo = null,
        Action<string>? onWarning = null,
        CancellationToken cancellationToken = default)
    {
        var testRuns = new Dictionary<string, TestRunFiles>();

        // Discover and group files by base name
        foreach (var filePath in Directory.GetFiles(folderPath, "test-report-*.json"))
        {
            var fileName = Path.GetFileName(filePath);
            var match = FilePattern.Match(fileName);
            if (!match.Success) continue;

            var timestamp = match.Groups[1].Value;
            var processName = match.Groups[2].Value;
            var suffix = match.Groups[3].Success ? match.Groups[3].Value : null;
            var baseName = $"test-report-{timestamp}-{processName}";

            if (!testRuns.TryGetValue(baseName, out var testRun))
            {
                testRun = new TestRunFiles(baseName);
                testRuns[baseName] = testRun;
            }

            // Categorize file by suffix
            if (suffix == null)
                testRun.MainReportPath = filePath;
            else if (suffix == "events-throughput")
                testRun.EventsThroughputPath = filePath;
            else if (suffix == "api-throughput")
                testRun.ApiThroughputPath = filePath;
            else if (suffix == "resource-metrics")
                testRun.ResourceMetricsPath = filePath;
            else if (suffix == "system-metrics")
                testRun.SystemMetricsPath = filePath;
            // postgres-metrics and rabbitmq-metrics not needed for comparison
        }

        onInfo?.Invoke($"Found {testRuns.Count} test run(s)");

        // Load each test run
        var results = new List<TestReport>();
        foreach (var (baseName, testRun) in testRuns)
        {
            if (testRun.MainReportPath == null)
            {
                onWarning?.Invoke($"  Skipped {baseName}: missing main report");
                continue;
            }

            try
            {
                var report = await LoadTestRunAsync(testRun, cancellationToken);
                if (report != null)
                {
                    results.Add(report);
                    onInfo?.Invoke($"  Loaded: {Path.GetFileName(testRun.MainReportPath)}");
                }
            }
            catch (Exception ex)
            {
                onWarning?.Invoke($"  Skipped {baseName}: {ex.Message}");
            }
        }

        return results;
    }

    private static async Task<TestReport?> LoadTestRunAsync(
        TestRunFiles files,
        CancellationToken cancellationToken)
    {
        // Load main report
        var mainJson = await File.ReadAllTextAsync(files.MainReportPath!, cancellationToken);
        var report = JsonSerializer.Deserialize<TestReport>(mainJson, JsonOptions);
        if (report == null) return null;

        // Load supplementary files and merge data
        var eventsSamples = await LoadEventsThroughputAsync(files.EventsThroughputPath, cancellationToken);
        var apiSamples = await LoadApiThroughputAsync(files.ApiThroughputPath, cancellationToken);
        var resourceSamples = await LoadResourceMetricsAsync(files.ResourceMetricsPath, cancellationToken);
        var systemSamples = await LoadSystemMetricsAsync(files.SystemMetricsPath, cancellationToken);

        // Create new TestReport with merged data (records are immutable, so we create a new one)
        return report with
        {
            EventsThroughputSamples = eventsSamples,
            ApiThroughputSamples = apiSamples,
            ProcessResourceSamples = resourceSamples,
            SystemResourceSamples = systemSamples
        };
    }

    private static async Task<IReadOnlyList<ThroughputMetricSample>> LoadEventsThroughputAsync(
        string? filePath,
        CancellationToken cancellationToken)
    {
        if (filePath == null || !File.Exists(filePath))
            return Array.Empty<ThroughputMetricSample>();

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var report = JsonSerializer.Deserialize<EventsThroughputReport>(json, JsonOptions);
        if (report?.Samples == null) return Array.Empty<ThroughputMetricSample>();

        return report.Samples.Select(s => new ThroughputMetricSample
        {
            Timestamp = DateTime.Parse(s.Timestamp),
            ElapsedSeconds = s.ElapsedSeconds,
            Rate = s.EventsPerSecond,
            CumulativeCount = s.TotalEvents
        }).ToList();
    }

    private static async Task<IReadOnlyList<ThroughputMetricSample>> LoadApiThroughputAsync(
        string? filePath,
        CancellationToken cancellationToken)
    {
        if (filePath == null || !File.Exists(filePath))
            return Array.Empty<ThroughputMetricSample>();

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var report = JsonSerializer.Deserialize<ApiThroughputReport>(json, JsonOptions);
        if (report?.Samples == null) return Array.Empty<ThroughputMetricSample>();

        return report.Samples.Select(s => new ThroughputMetricSample
        {
            Timestamp = DateTime.Parse(s.Timestamp),
            ElapsedSeconds = s.ElapsedSeconds,
            Rate = s.CallsPerSecond,
            CumulativeCount = s.TotalCalls
        }).ToList();
    }

    private static async Task<IReadOnlyList<ProcessResourceSample>> LoadResourceMetricsAsync(
        string? filePath,
        CancellationToken cancellationToken)
    {
        if (filePath == null || !File.Exists(filePath))
            return Array.Empty<ProcessResourceSample>();

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var report = JsonSerializer.Deserialize<ResourceMetricsFileReport>(json, JsonOptions);
        if (report?.Samples == null) return Array.Empty<ProcessResourceSample>();

        return report.Samples.Select(s => new ProcessResourceSample
        {
            Timestamp = DateTime.Parse(s.Timestamp),
            CpuPercent = s.CpuPercent,
            MemoryRssMb = s.MemoryRssMb,
            Threads = s.Threads
        }).ToList();
    }

    private static async Task<IReadOnlyList<SystemResourceSample>> LoadSystemMetricsAsync(
        string? filePath,
        CancellationToken cancellationToken)
    {
        if (filePath == null || !File.Exists(filePath))
            return Array.Empty<SystemResourceSample>();

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var report = JsonSerializer.Deserialize<SystemMetricsFileReport>(json, JsonOptions);
        if (report?.Samples == null) return Array.Empty<SystemResourceSample>();

        return report.Samples.Select(s => new SystemResourceSample
        {
            Timestamp = DateTime.Parse(s.Timestamp),
            CpuPercent = s.CpuPercent,
            MemoryUsedMb = s.MemoryUsedMb,
            MemoryTotalMb = s.MemoryTotalMb,
            MemoryPercent = s.MemoryPercent
        }).ToList();
    }

    /// <summary>
    /// Internal class to track files belonging to a single test run.
    /// </summary>
    private sealed class TestRunFiles
    {
        public string BaseName { get; }
        public string? MainReportPath { get; set; }
        public string? EventsThroughputPath { get; set; }
        public string? ApiThroughputPath { get; set; }
        public string? ResourceMetricsPath { get; set; }
        public string? SystemMetricsPath { get; set; }

        public TestRunFiles(string baseName) => BaseName = baseName;
    }
}
```

**Step 2: Verify file compiles**

Run: `dotnet build src/PerformanceTester.Reporting -c Release --verbosity quiet`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/PerformanceTester.Reporting/ComparisonGeneration/TestRunLoader.cs
git commit -m "feat(Reporting): add TestRunLoader to load supplementary JSON files

Mirrors Python's TestRun class behavior - discovers and loads all related
files (events-throughput, api-throughput, resource-metrics, system-metrics)
for each test run, then merges data into TestReport objects.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 3: Update CompareCommand to Use TestRunLoader

**Files:**
- Modify: `src/PerformanceTester.Cli/Commands/CompareCommand.cs`

**Step 1: Replace file loading logic with TestRunLoader**

Change lines 86-123 from:

```csharp
// Find test report JSON files (main reports, not supplementary)
var reportFiles = Directory.GetFiles(options.Folder, "test-report-*.json")
    .Where(f => !f.Contains("resource-metrics")
             && !f.Contains("events-throughput")
             && !f.Contains("api-throughput")
             && !f.Contains("postgres-metrics")
             && !f.Contains("rabbitmq-metrics")
             && !f.Contains("system-metrics"))
    .ToList();

if (reportFiles.Count == 0)
{
    consoleWriter.WriteWarning($"No test report files found in: {options.Folder}");
    consoleWriter.WriteLine("Run 'performance-tester test' first to generate reports.");
    return 1;
}

consoleWriter.WriteInfo($"Found {reportFiles.Count} test report(s)");

// Load test reports
var testReports = new List<TestReport>();
foreach (var file in reportFiles)
{
    try
    {
        var json = await File.ReadAllTextAsync(file, cancellationToken);
        var report = JsonSerializer.Deserialize<TestReport>(json, JsonOptions);
        if (report is not null)
        {
            testReports.Add(report);
            consoleWriter.WriteInfo($"  Loaded: {Path.GetFileName(file)}");
        }
    }
    catch (JsonException ex)
    {
        consoleWriter.WriteWarning($"  Skipped (invalid JSON): {Path.GetFileName(file)} - {ex.Message}");
    }
}
```

To:

```csharp
// Load test reports with supplementary files (like Python compare_test_results.py)
var testReports = await TestRunLoader.LoadTestReportsAsync(
    options.Folder,
    onInfo: msg => consoleWriter.WriteInfo(msg),
    onWarning: msg => consoleWriter.WriteWarning(msg),
    cancellationToken: cancellationToken);
```

**Step 2: Add using statement**

Add to top of file:
```csharp
using PerformanceTester.Reporting.ComparisonGeneration;
```

**Step 3: Remove unused JsonOptions and using statements**

Remove these lines since TestRunLoader handles JSON options internally:
```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    Converters = { new Iso8601DateTimeConverter() }
};
```

And remove:
```csharp
using System.Text.Json;
using PerformanceTester.Reporting.Shared.Utilities;
```

**Step 4: Verify CLI compiles and runs**

Run: `dotnet build src/PerformanceTester.Cli -c Release --verbosity quiet`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add src/PerformanceTester.Cli/Commands/CompareCommand.cs
git commit -m "refactor(Cli): use TestRunLoader to load supplementary files

Replaces manual file loading with TestRunLoader which discovers and loads
all related files for each test run, matching Python behavior.

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Task 4: Verify End-to-End Functionality

**Step 1: Run comparison on existing test results**

Run from WSL2:
```bash
cd /workspace/performance-tester-dotnet/src/PerformanceTester.Cli
dotnet run -c Release -- compare --folder ../../test-results
```

Expected: Report generates with actual metric values (not all zeros)

**Step 2: Verify metrics in generated report**

Check the generated markdown file for:
- Event throughput values (Avg, Peak, Min, Std Dev, CV%)
- API throughput values
- CPU usage values
- Memory usage values

All should show actual numbers instead of 0.00.

**Step 3: Compare with Python output**

Run Python comparison:
```bash
cd /workspace/performance-tester
python compare_test_results.py --folder ../performance-tester-dotnet/test-results
```

Verify .NET and Python reports show similar structure and non-zero values.

---

## Task 5: Update Documentation

**Files:**
- Modify: `src/PerformanceTester.Cli/README.md` (if exists, otherwise skip)

**Step 1: Document the compare command behavior**

Add section explaining that compare command:
1. Discovers all test report files in the specified folder
2. Groups files by timestamp (main report + supplementary files)
3. Loads and merges data from:
   - Main report (`.json`)
   - Events throughput (`.events-throughput.json`)
   - API throughput (`.api-throughput.json`)
   - Resource metrics (`.resource-metrics.json`)
   - System metrics (`.system-metrics.json`)
4. Generates comparison markdown with statistics from supplementary files

**Step 2: Commit**

```bash
git add -A
git commit -m "docs: document compare command supplementary file loading

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"
```

---

## Summary

| Task | Description | Files |
|------|-------------|-------|
| 1 | Create supplementary file models | `SupplementaryReportModels.cs` (new) |
| 2 | Create TestRunLoader | `TestRunLoader.cs` (new) |
| 3 | Update CompareCommand | `CompareCommand.cs` (modify) |
| 4 | Verify end-to-end | Manual testing |
| 5 | Update documentation | README (if exists) |

**Total estimated new code:** ~400 lines
**Total modified code:** ~40 lines (CompareCommand simplification)
