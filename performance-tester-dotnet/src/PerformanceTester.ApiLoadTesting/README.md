# PerformanceTester.ApiLoadTesting

HTTP API load testing with k6 integration, real-time metrics parsing, and comprehensive result aggregation. Implements Phase 2 of the Performance Tester .NET implementation.

## Features

### k6 Integration

- Executes k6 binary as subprocess for load testing
- Real-time JSON output parsing (not post-processing)
- Automatic k6 script generation from parameters
- Exit code validation and error detection
- Stderr monitoring for error messages

### Load Test Configuration

- Configurable test duration (e.g., 30s, 2m)
- Configurable virtual users (concurrent requests)
- Target endpoint URL
- Supports GET requests with status 200 validation

### Real-Time Metrics Collection

- Parses k6 JSON output line-by-line as test runs
- Tracks metrics: `http_reqs`, `http_req_duration`, `http_req_failed`, `vus`
- Calculates throughput samples during test execution
- Simple service pattern: single `await` returns complete result

### Result Aggregation

- Total requests and failed requests
- Average, P95, P99 request duration
- Requests per second (throughput)
- Test duration
- Collection of throughput samples over time

## Prerequisites

**k6 Binary Installation:**

```bash
# Linux (snap)
sudo snap install k6

# macOS (Homebrew)
brew install k6

# Windows (Chocolatey)
choco install k6

# Or download from: https://k6.io/docs/getting-started/installation/
```

**Verify Installation:**

```bash
k6 version
```

## Installation

Add project reference:

```bash
dotnet add reference ../PerformanceTester.ApiLoadTesting/PerformanceTester.ApiLoadTesting.csproj
```

## Usage

### Dependency Injection Setup

```csharp
using PerformanceTester.ApiLoadTesting;

var services = new ServiceCollection();

// Register API load testing services
services.AddApiLoadTesting();

var serviceProvider = services.BuildServiceProvider();
```

### Running a Load Test

```csharp
// Get load tester from DI
var loadTester = serviceProvider.GetRequiredService<IApiLoadTester>();

// Run test and await result
var result = await loadTester.StartTestAsync(
    targetUrl: "http://localhost:8094/kpi",
    duration: TimeSpan.FromSeconds(30),
    virtualUsers: 10,
    cancellationToken);

Console.WriteLine($"Total Requests: {result.TotalRequests}");
Console.WriteLine($"Failed Requests: {result.FailedRequests}");
Console.WriteLine($"Average Duration: {result.AverageRequestDurationMs}ms");
Console.WriteLine($"P95 Duration: {result.P95RequestDurationMs}ms");
Console.WriteLine($"P99 Duration: {result.P99RequestDurationMs}ms");
Console.WriteLine($"Requests/sec: {result.RequestsPerSecond}");
Console.WriteLine($"Throughput Samples: {result.ThroughputSamples.Count}");
```

## Integration Testing

This project includes comprehensive integration tests:

```bash
# Run all tests (requires k6 installed)
dotnet test PerformanceTester.ApiLoadTesting.IntegrationTests

# Run specific test class
dotnet test --filter "FullyQualifiedName~ApiLoadTesterIntegrationTests"
```

Tests validate:

- k6 execution and result parsing
- Metrics aggregation (requests, duration, percentiles)
- Throughput sample collection
- Error handling (invalid URLs, missing k6 binary)

## Architecture

### Vertical Slice Architecture (VSA)

- ApiLoadTesting slice owns ApiLoadTestResult and ApiThroughputSample models (producer-owned contracts)
- Exposes single interface: `IApiLoadTester`
- No dependencies on other project slices (only System.Diagnostics.Process)

### Design Decisions

**Real-Time Parsing vs Post-Processing:**

- k6 outputs JSON to stdout in real-time (`--out json=-`)
- We parse line-by-line as k6 runs (not from temporary file)
- More efficient (no disk I/O, lower memory)
- Immediate error detection from stderr stream

**k6 Script Generation:**

- Template pattern with placeholder replacement
- Script written to temporary file in system temp directory
- File cleanup happens in finally block

**Simple Async Pattern:**

- `StartTestAsync()` executes k6 and returns `Task<ApiLoadTestResult>`
- Single await returns complete result (no lifecycle management)
- Clean API: no Start/Stop/GetResult ceremony

**Thread Safety:**

- Metrics aggregation happens in single thread (no concurrency issues)
- ConcurrentBag used for throughput sample storage

## Dependencies

- Microsoft.Extensions.DependencyInjection.Abstractions 9.0.0
- Microsoft.Extensions.Logging.Abstractions 9.0.0
- System.Diagnostics.Process (built-in)
- System.Text.Json (built-in)

## Python Source Reference

This implementation is based on `performance-tester/service-tester.py` (k6 execution and metrics parsing, lines 400-650) from the original Python implementation.

Key improvements over Python:

- **Real-time parsing** (stdout streaming) vs. post-processing (file read)
- **Simple async pattern** (single await) vs. subprocess.run with blocking
- **Stderr monitoring** (real-time error detection) vs. exit code only
- **Strongly-typed models** (ApiLoadTestResult, ApiThroughputSample) vs. dict

## Troubleshooting

### Tests fail with "k6 binary not found"

- Install k6: https://k6.io/docs/getting-started/installation/
- Verify installation: `k6 version`
- Ensure k6 is in system PATH

### k6 execution fails with exit code 99

- k6 test setup/teardown failed
- Check stderr output in logs
- Verify target URL is accessible

### Metrics appear incorrect

- Verify k6 JSON output format hasn't changed
- Check k6 version matches expected format
- Enable debug logging to see parsed metrics

### Throughput samples empty

- k6 may not emit `vus` metrics for very short tests
- Try longer test duration (e.g., 10+ seconds)
- Check k6 JSON output for expected metric types

---

Part of the Performance Tester .NET implementation.
