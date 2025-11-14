# PerformanceTester.Reporting

Phase 3 slice providing report generation, charting, and comparison analysis for performance test results.

## Purpose

Generates comprehensive reports from performance test data:
- JSON reports with test metrics, system info, and statistics
- PNG charts with 5-subplot visualizations (ScottPlot)
- Comparison reports with rankings and medal awards
- Cross-platform hardware detection (Windows/Linux/WSL2)

## Architecture

### Strategy Pattern for OS Detection

Uses **PerformanceTester.Common** for platform detection:
- `WindowsSystemInfoDetector` - WMI-based hardware detection (Windows)
- `LinuxSystemInfoDetector` - /proc parsing + WSL2 support (Linux)
- Platform selection at DI registration (no `#if` directives)

### Key Services

- `ISystemInfoDetector` - Hardware and OS information detection
- `IReportGenerator` - JSON report generation
- `IChartGenerator` - PNG chart generation (ScottPlot)
- `IComparisonReportGenerator` - Comparison analysis and rankings

## Usage

### DI Registration

```csharp
builder.Services.AddReporting();
```

Platform detection happens automatically at registration time.

### Generate Report

```csharp
var reportGenerator = host.Services.GetRequiredService<IReportGenerator>();

await reportGenerator.GenerateReportAsync(
    outputDirectory: "/path/to/reports",
    testReport: testReport);
```

### Generate Chart

```csharp
var chartGenerator = host.Services.GetRequiredService<IChartGenerator>();

await chartGenerator.GenerateChartAsync(
    outputPath: "/path/to/chart.png",
    testReport: testReport);
```

### Generate Comparison Report

```csharp
var comparisonGenerator = host.Services.GetRequiredService<IComparisonReportGenerator>();

await comparisonGenerator.GenerateComparisonAsync(
    reportsDirectory: "/path/to/reports/",
    outputPath: "/path/to/comparison.md");
```

## Dependencies

- **PerformanceTester.Common** - OS platform detection
- **Phase 2 Slices** - Event publishing, consuming, monitoring models
- **MathNet.Numerics** - Statistical calculations (CV%, stddev, etc.)
- **ScottPlot 5.0** - Chart generation
- **System.Management** - Windows WMI queries (Windows only)

## Testing

Integration tests verify:
- Cross-platform hardware detection (CPU, RAM, disks)
- WSL2 detection and PowerShell host queries
- Report JSON serialization
- Chart PNG generation
- Comparison statistics and rankings

```bash
dotnet test src/PerformanceTester.Reporting.IntegrationTests
```

## Platform Support

| Platform | CPU | RAM | Disks | WSL Detection |
|----------|-----|-----|-------|---------------|
| Windows  | ✅ WMI | ✅ WMI | ✅ WMI | N/A |
| Linux    | ✅ /proc/cpuinfo | ✅ /proc/meminfo | ✅ lsblk | ✅ /proc/version |
| WSL2     | ✅ /proc/cpuinfo | ✅ PowerShell→Host | ✅ PowerShell→Host | ✅ Detected |

## Design Decisions

### Why Separate Windows/Linux Implementations?

- **Clean separation** - No `#if` directives in service code
- **Testable** - Easy to mock `IOSPlatformDetector`
- **Maintainable** - Each detector contains only platform-specific code
- **Consistent** - Same pattern as `Infrastructure.ProcessFinding`

### Why Use Common Library?

- **Single source of truth** - OS detection logic in one place
- **Reusable** - Available to all projects (netstandard2.0)
- **Extensible** - Easy to add new platforms (macOS, etc.)

## Implementation Status

### ✅ Completed (Tasks 1-9, 18-22, 24-25)
- Production project setup
- Test project setup
- Models (SystemInfo, TestReport, ThroughputReport, ResourceMetricsReport)
- StatisticsCalculator with MathNet.Numerics
- SystemInfoDetector interface and platform-specific implementations
- DI extension method with OS detection
- Test infrastructure (IntegrationTest, ReportingSystem, ReportingAssertions)
- SystemInfoDetector integration tests

### 🚧 Pending (Tasks 10-17, 23)
- ReportGenerator implementation (Task 10-11)
- ChartGenerator implementation (Task 12-14)
- ComparisonReportGenerator implementation (Task 15-17)
- Report generation integration tests (Task 23)

## Related Documentation

- [Phase 3 Implementation Plan](../../docs/plans/12.PHASE_3_REPORTING_PLAN.md)
- [Integration Testing Guide](../../docs/02.INTEGRATION_TEST_DESIGN.md)
- [VSA Architecture](../../docs/04.IMPLEMENTATION_ORDER_VSA.md)
- [Python File Formats Specification](/workspace/performance-tester/PYTHON_FILE_FORMATS.md)
