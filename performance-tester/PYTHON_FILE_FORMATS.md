# Python Performance Testing File Formats - Complete Specification

## Overview
The Python performance testing framework generates multiple JSON files and PNG charts to document service performance metrics. The .NET implementation must generate equivalent files with **identical** structures and naming conventions.

---

## File Naming Convention

All report files follow this pattern:
```
test-report-{TIMESTAMP}-{PROCESS_NAME}.{SUFFIX}
```

Where:
- `{TIMESTAMP}`: `YYYYMMDD_HHMMSS` format (e.g., `20251113_142530`)
- `{PROCESS_NAME}`: Sanitized process name (lowercase, no special chars except dashes/dots)
- `{SUFFIX}`: File type extension (see below)

**Examples:**
```
test-report-20251113_142530-goReferenceService.json
test-report-20251113_142530-goReferenceService.resource-metrics.json
test-report-20251113_142530-goReferenceService.api-throughput.json
test-report-20251113_142530-goReferenceService.events-throughput.json
test-report-20251113_142530-goReferenceService.system-metrics.json
test-report-20251113_142530-goReferenceService.rabbitmq-metrics.json
test-report-20251113_142530-goReferenceService.postgres-metrics.json
test-report-20251113_142530-goReferenceService.chart.png
```

### Process Name Sanitization Rules
Applied to produce `{PROCESS_NAME}`:
1. Remove file extensions (.jar, .dll, .exe)
2. Replace spaces and special chars with dashes
3. Remove consecutive dashes
4. Remove leading/trailing dashes
5. Convert to lowercase

**Examples:**
- `java21SpringBootGraalReferenceService` → `java21springbootgraalreferenceservice`
- `dotnet9AotReferenceService` → `dotnet9aotreferenceservice`
- `pythonReferenceService` → `pythonreferenceservice`

---

## 1. Main Report File (test-report-{timestamp}-{process}.json)

**JSON Indentation:** 2 spaces
**Timestamp Format in JSON:** `YYYY-MM-DD HH:MM:SS` (ISO datetime string)

```json
{
  "test_date": "2025-11-13 14:25:30",
  "total_runtime_seconds": 125.456,
  
  "system": {
    // Full system_info object (see System Information section below)
  },
  
  "phase_timestamps": {
    "phase1_publish_start": 0.123,
    "phase1_publish_end": 5.456,
    "phase2_consume_start": 5.789,
    "phase2_consume_end": 45.012,
    "phase3_api_start": 45.345,
    "phase3_api_end": 75.678
  },
  
  "monitored_process": {
    "pid": 12345,
    "name": "pythonreferenceservice",
    "port": 8099
  },
  
  "configuration": {
    "num_events": 10000,
    "api_duration": "30s",
    "api_concurrent_workers": 1,
    "rabbitmq_exchange": "instrument",
    "consumer_queue": "service-tester",
    "api_endpoint": "http://localhost:8099/kpi",
    "publish_event_type": "instrument.status.changed",
    "consume_event_type": "instrumentstatus.kpi.updated"
  },
  
  "results": {
    "phase1_publish": {
      "duration_seconds": 5.333,
      "throughput_events_per_sec": 1875.22
    },
    "phase2_consume": {
      "duration_seconds": 39.223,
      "throughput_events_per_sec": 254.98
    },
    "phase3_api": {
      "duration_seconds": 30.333,
      "total_requests": 3245,
      "throughput_calls_per_sec": 107.00,
      "success_count": 3245,
      "success_percentage": 100.0,
      "error_count": 0,
      "error_percentage": 0.0
    }
  }
}
```

### Field Details:
- `test_date`: ISO datetime string (YYYY-MM-DD HH:MM:SS)
- `total_runtime_seconds`: Total test duration (3 decimal places)
- `phase_timestamps`: Optional; contains start/end times for each phase (3 decimal places)
- `monitored_process`: Optional; contains process information
- All throughput values: 2 decimal places
- All percentage values: 1 decimal place
- All duration values: 3 decimal places

---

## 2. Events Throughput Report (.events-throughput.json)

**JSON Indentation:** 2 spaces
**Timestamp Format in Samples:** ISO 8601 format with timezone

```json
{
  "test_date": "2025-11-13 14:25:30",
  "sampling_interval_ms": 100,
  
  "samples": [
    {
      "timestamp": "2025-11-13T14:25:30.100000+00:00",
      "elapsed_seconds": 0.1,
      "total_events": 45,
      "events_per_second": 450.00
    },
    {
      "timestamp": "2025-11-13T14:25:30.200000+00:00",
      "elapsed_seconds": 0.2,
      "total_events": 95,
      "events_per_second": 500.00
    }
    // ... more samples at 100ms intervals
  ],
  
  "summary": {
    "avg_events_per_second": 254.98,
    "peak_events_per_second": 523.45,
    "min_events_per_second": 45.23,
    "std_dev_events_per_second": 125.67,
    "cv_events_per_second": 49.32,
    "avg_response_time_ms": 3.921,
    "total_samples": 392,
    "total_events": 10000
  }
}
```

### Sample Structure Details:
- `timestamp`: ISO 8601 datetime with timezone
- `elapsed_seconds`: Time since monitoring start (3 decimal places)
- `total_events`: Cumulative event count (integer)
- `events_per_second`: Rate for this period (2 decimal places)

### Summary Statistics:
- **avg_events_per_second**: Mean throughput (2 decimal places)
- **peak_events_per_second**: Maximum throughput (2 decimal places)
- **min_events_per_second**: Minimum throughput excluding zeros (2 decimal places)
- **std_dev_events_per_second**: Standard deviation (2 decimal places)
- **cv_events_per_second**: Coefficient of variation % = (stdev / mean) * 100 (2 decimal places)
- **avg_response_time_ms**: 1000.0 / avg_events_per_second (3 decimal places)
- **total_samples**: Number of samples collected (integer)
- **total_events**: Total events processed (integer)

### Calculation Formulas:
```
CV = (standard_deviation / mean) * 100
avg_response_time_ms = 1000.0 / avg_events_per_second
```

**Zero Value Handling:** 
- Min calculation filters out zero values (post-test measurements)
- Average calculation includes all values for accurate representation

---

## 3. API Throughput Report (.api-throughput.json)

**Identical structure to events-throughput.json, but with API metrics:**

```json
{
  "test_date": "2025-11-13 14:25:30",
  "sampling_interval_ms": 100,
  
  "samples": [
    {
      "timestamp": "2025-11-13T14:25:45.100000+00:00",
      "elapsed_seconds": 0.1,
      "total_calls": 12,
      "calls_per_second": 120.00
    },
    // ... more samples
  ],
  
  "summary": {
    "avg_calls_per_second": 107.23,
    "peak_calls_per_second": 245.67,
    "min_calls_per_second": 23.45,
    "std_dev_calls_per_second": 45.23,
    "cv_calls_per_second": 42.17,
    "avg_response_time_ms": 9.325,
    "total_samples": 303,
    "total_calls": 3245
  }
}
```

**Key Differences from Events Throughput:**
- `calls_per_second` instead of `events_per_second`
- `total_calls` instead of `total_events`
- `avg_calls_per_second` instead of `avg_events_per_second`
- All other metrics named with "calls" instead of "events"

---

## 4. Resource Metrics Report (.resource-metrics.json)

**Monitors process CPU and memory usage**

```json
{
  "test_date": "2025-11-13 14:25:30",
  "process_info": {
    "pid": 12345,
    "name": "pythonreferenceservice",
    "port": 8099
  },
  "sampling_interval_ms": 500,
  
  "samples": [
    {
      "timestamp": "2025-11-13T14:25:30.500000+00:00",
      "cpu_percent": 25.50,
      "memory_rss_mb": 125.75,
      "threads": 5
    },
    {
      "timestamp": "2025-11-13T14:25:31.000000+00:00",
      "cpu_percent": 28.30,
      "memory_rss_mb": 128.25,
      "threads": 5
    }
    // ... more samples
  ],
  
  "summary": {
    "avg_cpu_percent": 22.45,
    "peak_cpu_percent": 45.67,
    "avg_memory_rss_mb": 135.23,
    "peak_memory_rss_mb": 185.75,
    "total_samples": 250
  }
}
```

### Sample Fields:
- `timestamp`: ISO 8601 datetime
- `cpu_percent`: CPU percentage (2 decimal places)
- `memory_rss_mb`: RSS memory in MB (2 decimal places)
- `threads`: Thread count (integer)

### Summary Statistics:
- All values use 2 decimal places
- RSS = Resident Set Size (actual physical memory used)

---

## 5. Container Metrics Report (.rabbitmq-metrics.json, .postgres-metrics.json)

**Monitors Docker container CPU and memory**

```json
{
  "test_date": "2025-11-13 14:25:30",
  "container_info": {
    "name": "performancetest-rabbitmq",
    "id": "abc123def456"
  },
  "sampling_interval_ms": 3000,
  
  "samples": [
    {
      "timestamp": "2025-11-13T14:25:30.000000+00:00",
      "cpu_percent": 15.25,
      "memory_mb": 256.75
    },
    {
      "timestamp": "2025-11-13T14:25:33.000000+00:00",
      "cpu_percent": 18.50,
      "memory_mb": 275.25
    }
    // ... more samples at 3-second intervals
  ],
  
  "summary": {
    "avg_cpu_percent": 16.75,
    "peak_cpu_percent": 28.50,
    "avg_memory_mb": 265.30,
    "peak_memory_mb": 312.75,
    "total_samples": 125
  }
}
```

### Differences from Resource Metrics:
- Uses container name/ID instead of process info
- Memory is `memory_mb` (not `memory_rss_mb`)
- Typical sampling interval is 3000ms (slower for containers)
- No thread count

---

## 6. System Metrics Report (.system-metrics.json)

**Monitors overall system CPU and memory usage**

```json
{
  "test_date": "2025-11-13 14:25:30",
  "cpu_count": 8,
  "sampling_interval_ms": 500,
  "is_wsl2": true,
  "windows_host_total_ram_mb": 16384.00,
  
  "samples": [
    {
      "timestamp": "2025-11-13T14:25:30.500000+00:00",
      "cpu_percent": 35.50,
      "memory_used_mb": 6245.75,
      "memory_total_mb": 16384.00,
      "memory_percent": 38.10
    },
    {
      "timestamp": "2025-11-13T14:25:31.000000+00:00",
      "cpu_percent": 38.25,
      "memory_used_mb": 6512.50,
      "memory_total_mb": 16384.00,
      "memory_percent": 39.75
    }
    // ... more samples
  ],
  
  "summary": {
    "avg_cpu_percent": 32.15,
    "peak_cpu_percent": 65.75,
    "min_cpu_percent": 5.25,
    "avg_memory_used_mb": 6350.25,
    "peak_memory_used_mb": 7245.75,
    "avg_memory_percent": 38.75,
    "peak_memory_percent": 44.25,
    "total_samples": 250
  }
}
```

### Special Fields:
- `cpu_count`: Number of logical CPU cores (integer)
- `is_wsl2`: Boolean flag (only present if true)
- `windows_host_total_ram_mb`: Windows host RAM in MB (2 decimal places) - **only for WSL2**
- `memory_total_mb`: Total system memory (2 decimal places)
- `memory_percent`: Memory usage as percentage (2 decimal places)

### WSL2 Detection:
- If running in WSL2, queries Windows host for accurate values
- Uses PowerShell to get Windows system info
- Falls back to Linux /proc if Windows query fails

---

## 7. System Information Object (embedded in main report)

**Complete structure from system_info.py:**

```json
{
  "os": "Linux",
  "os_release": "6.6.87.2-microsoft-standard-WSL2",
  "os_version": "#1 SMP Wed Aug 14 21:50:04 UTC 2024",
  
  "wsl_version": "WSL2",
  
  "cpu": {
    "model": "Intel(R) Core(TM) i9-13900K CPU @ 3.00GHz",
    "logical_processors": 8,
    "physical_processors": 4,
    "speed_mhz": 4200.00
  },
  
  "ram": {
    "total_gb": 32.00,
    "speed": "3600 MHz",
    "type": "DDR4",
    "manufacturer": "SK Hynix"
  },
  
  "disks": [
    {
      "name": "Samsung 980 Pro",
      "size": "2.0T",
      "type": "SSD (NVMe)",
      "model": "Samsung 980 Pro"
    },
    {
      "name": "WDC WD10EZEX",
      "size": "1.0T",
      "type": "HDD (SATA)",
      "model": "WDC WD10EZEX"
    }
  ]
}
```

### CPU Information:
- `model`: CPU model string from /proc/cpuinfo
- `logical_processors`: Total logical CPU count (integer)
- `physical_processors`: Physical processor count (integer)
- `speed_mhz`: CPU frequency in MHz (2 decimal places)

### RAM Information (WSL2 queried via PowerShell, Linux via dmidecode):
- `total_gb`: Total RAM in GB (2 decimal places)
- `speed`: RAM speed string (e.g., "3600 MHz")
- `type`: RAM type (DDR3, DDR4, DDR5, etc.)
- `manufacturer`: RAM manufacturer (may be omitted if unavailable)

### Disk Information:
- `name`: Device name or model
- `size`: Human-readable size (e.g., "2.0T", "500.0G")
- `type`: "SSD", "HDD", or with bus type (e.g., "SSD (NVMe)", "HDD (SATA)")
- `model`: Full model string (may be omitted)
- **Only disks >= 500GB are reported**

### WSL2 Special Handling:
- Queries Windows host via PowerShell for accurate hardware info
- Disks are from Windows physical drives (not WSL2 virtual disks)
- Memory values are from Windows OS, not WSL2 VM

---

## 8. Chart File (.chart.png)

**Matplotlib PNG image with 5 subplots:**

1. **Subplot 1 - Throughput (Events + API)**
   - X-axis: Time (HH:MM:SS format)
   - Y-axis: Per-second rate
   - Lines: Events/sec (green) and API calls/sec (orange)
   - Shaded areas under lines
   - Average lines (dashed) with statistics in legend
   - Phase boundaries marked with vertical lines

2. **Subplot 2 - Service CPU & RAM**
   - Dual Y-axes: CPU% (left, blue), RAM MB (right, red)
   - Lines: CPU percent and RAM usage
   - Shaded areas
   - Average lines with statistics

3. **Subplot 3 - RabbitMQ Metrics**
   - Same dual-axis format: CPU% (purple) and RAM MB (orange)
   - Container monitoring data

4. **Subplot 4 - PostgreSQL Metrics**
   - Same dual-axis format: CPU% (teal) and RAM MB (gold)
   - Container monitoring data

5. **Subplot 5 - Overall System Metrics**
   - Dual Y-axes: CPU% (dark gray, 0-100%), RAM MB (red)
   - Full system CPU and memory
   - CPU limited to 0-100% range
   - RAM limited to 0-total_memory range

### Chart Title Format:
```
Performance Metrics - {process_name} - {Month} {Day}, {Year} at HH:MM:SS
```
Example: `Performance Metrics - pythonreferenceservice - November 13, 2025 at 14:25:30`

### Legend Format for Throughput:
```
Events Avg: {avg:.1f} ({response_time:.2f}ms)
Min: {min:.1f}
Max: {max:.1f}
Mode: {mode}
Std Dev: {std_dev:.1f}
CV: {cv:.1f}%
```

### Legend Format for Resources:
```
Avg: {avg:.1f} {unit}
Min: {min:.1f} {unit}
Max: {max:.1f} {unit}
Mode: {mode} {unit}
```

### Phase Boundary Lines:
- Phase 1→2: Green vertical line
- Phase 2→3: Blue vertical line
- Phase 3 end: Orange vertical line
- Labels: "Consume: {num_events}" and "API: {duration} ({workers}w)"

### Color Scheme:
- Events/sec: #2ecc71 (green), avg line #27ae60
- API calls/sec: #e67e22 (orange), avg line #d35400
- Service CPU: #3498db (blue), avg #2980b9
- Service RAM: #e74c3c (red), avg #c0392b
- RabbitMQ CPU: #9b59b6 (purple), avg #8e44ad
- RabbitMQ RAM: #e67e22 (orange), avg #d35400
- PostgreSQL CPU: #16a085 (teal), avg #138d75
- PostgreSQL RAM: #f39c12 (gold), avg #e67e22
- System CPU: #34495e (dark gray), avg #2c3e50
- System RAM: #c0392b (dark red), avg #a93226

---

## 9. Comparison Report (.md format)

**Generated by compare_test_results.py** - Markdown format

```markdown
# Test Results Comparison Report

**Generated:** 2025-11-13 14:25:30

## Test Environment

**Hardware & System:**
- **CPU Model:** Intel(R) Core(TM) i9-13900K CPU @ 3.00GHz
- **CPU Cores:** 8 cores
- **Total Memory:** 32.00 GB DDR4 @ 3600 MHz
- **Memory Manufacturer:** SK Hynix
- **Platform:** Linux 6.6.87.2-microsoft-standard-WSL2
- **Python Version:** 3.13.0

**Storage (2 physical drive(s) on Windows host):**

**Drive 1:** Samsung 980 Pro
- **Type:** SSD (NVMe)
- **Model:** Samsung 980 Pro
- **Capacity:** 2.0T

**Drive 2:** WDC WD10EZEX
- **Type:** HDD (SATA)
- **Model:** WDC WD10EZEX
- **Capacity:** 1.0T

**Test Run Information:**
- **Start Time:** 2025-11-13 14:25:30
- **End Time:** 2025-11-13 14:45:30
- **Total Duration:** 20m 0s
- **Number of Services Tested:** 3

## Test Runs Overview (sorted by Runtime - lower is better)

| # | Test Date | Process | Runtime (s) | Events | API Duration | Workers |
|---|-----------|---------|-------------|--------|--------------|---------|
| 1 | 2025-11-13 14:25:30 | goReferenceService | 75.45 🥇 | 10000 | 30s | 1 |
| 2 | 2025-11-13 14:35:45 | pythonReferenceService | 105.23 🥈 | 10000 | 30s | 1 |
| 3 | 2025-11-13 14:45:30 | dotnet9aotreferenceservice | 125.67 🥉 | 10000 | 30s | 1 |

## Throughput Comparison

### Event Processing Throughput (sorted by Avg - higher is better)

| # | Process | Avg (events/s) | Peak (events/s) | Min (events/s)* | Std Dev** | CV%*** |
|---|---------|----------------|-----------------|-----------------|-----------|--------|
| 1 | goReferenceService | 254.98 🥇 | 523.45 🥇 | 125.34 🥇 | 45.23 🥇 | 17.76 🥇 |
| 2 | pythonReferenceService | 198.75 🥈 | 412.50 🥈 | 89.23 🥈 | 67.45 🥈 | 33.92 🥈 |
| 3 | dotnet9aotreferenceservice | 145.50 🥉 | 298.75 🥉 | 42.15 🥉 | 89.23 🥉 | 61.34 🥉 |

### API Throughput (sorted by Avg - higher is better)

| # | Process | Total Requests | Avg (calls/s) | Peak (calls/s) | Min (calls/s)* | Std Dev** | CV%*** | Avg Response Time (ms) |
|---|---------|----------------|---------------|----------------|----------------|-----------|--------|------------------------|
| 1 | goReferenceService | 3245 | 107.23 🥇 | 245.67 🥇 | 23.45 🥇 | 45.23 🥇 | 42.17 🥇 | 9.325 🥇 |
| 2 | pythonReferenceService | 2847 | 93.50 🥈 | 198.75 🥈 | 18.25 🥈 | 56.78 🥈 | 60.75 🥈 | 10.695 🥈 |
| 3 | dotnet9aotreferenceservice | 2456 | 80.75 🥉 | 156.23 🥉 | 12.45 🥉 | 67.45 🥉 | 83.50 🥉 | 12.375 🥉 |

## Resource Usage Comparison

### Process CPU Usage (sorted by Avg CPU % - lower is better)

| # | Process | Avg CPU % | Peak CPU % |
|---|---------|-----------|------------|
| 1 | goReferenceService | 12.45 🥇 | 28.50 🥇 |
| 2 | pythonReferenceService | 24.75 🥈 | 45.25 🥈 |
| 3 | dotnet9aotreferenceservice | 18.25 🥉 | 38.75 🥉 |

### Process Memory Usage (sorted by Avg - lower is better)

| # | Process | Avg Memory (MB) | Peak Memory (MB) |
|---|---------|-----------------|------------------|
| 1 | goReferenceService | 45.25 🥇 | 78.50 🥇 |
| 2 | dotnet9aotreferenceservice | 125.75 🥈 | 185.25 🥈 |
| 3 | pythonReferenceService | 185.50 🥉 | 245.75 🥉 |

### System-Wide Metrics (sorted by Avg System CPU % - lower is better)

| # | Process | Avg System CPU % | Peak System CPU % | Avg System Memory (MB) |
|---|---------|------------------|-------------------|------------------------|
| 1 | goReferenceService | 18.75 🥇 | 42.50 🥇 | 4256.75 🥇 |
| 2 | pythonReferenceService | 28.50 🥈 | 52.75 🥈 | 5125.50 🥈 |
| 3 | dotnet9aotreferenceservice | 22.50 🥉 | 48.25 🥉 | 4675.25 🥉 |

## Performance Highlights

- **Fastest Event Processing:** goReferenceService - 254.98 events/s
- **Fastest API Throughput:** goReferenceService - 107.23 calls/s
- **Most Stable Event Processing:** goReferenceService - CV: 17.76%
- **Most Stable API Throughput:** goReferenceService - CV: 42.17%
- **Lowest Average CPU Usage:** goReferenceService - 12.45%
- **Lowest Peak Memory Usage:** goReferenceService - 78.50 MB
```

### Comparison Report Features:
- Markdown format
- Medal emojis (🥇🥈🥉) for top 3 performers
- Separate "lower is better" and "higher is better" sorting
- Notes explaining variability metrics (Min, Std Dev, CV%)
- Performance highlights summary

---

## JSON Precision Rules Summary

| Field Type | Decimal Places | Notes |
|------------|-----------------|-------|
| Time (seconds) | 3 | Phase timestamps, elapsed_seconds |
| Throughput (per second) | 2 | events/s, calls/s, events_per_second, calls_per_second |
| CPU Percent | 2 | All CPU measurements |
| Memory (MB/GB) | 2 | RAM values, disk sizes |
| Percentages | 1 | Success%, error%, CV%, memory% |
| Response Time (ms) | 3 | avg_response_time_ms |
| Timestamps | ISO 8601 | With timezone for datetime fields |

---

## Timestamp Formats

**In Main Report:**
```
YYYY-MM-DD HH:MM:SS
2025-11-13 14:25:30
```

**In Sample Timestamps (ISO 8601):**
```
YYYY-MM-DDTHH:MM:SS.ffffff+00:00
2025-11-13T14:25:30.100000+00:00
```

---

## Zero Handling

- **Throughput Min Values:** Exclude zeros from min calculation
- **Throughput Average:** Include zeros in average calculation
- **System Metrics:** Keep zero samples as valid data points
- **Resource Metrics:** All samples treated as valid (no zero exclusion)

---

## Key Metric Definitions

### Coefficient of Variation (CV)
```
CV% = (standard_deviation / mean) * 100
```
- CV < 10%: Very stable performance
- CV 10-20%: Moderate variability
- CV > 20%: High variability/bursty behavior

### Response Time
```
response_time_ms = 1000.0 / throughput_per_second
```

### Throughput Samples
- Collected at regular intervals (100ms for processes, 500ms for system, 3000ms for containers)
- Each sample includes cumulative totals and instantaneous rates

---

## File Ordering in Reports

Files are typically accessed in this order:
1. Main report (test-report-{timestamp}-{process}.json) - Summary
2. Events throughput (.events-throughput.json) - Phase 2 metrics
3. API throughput (.api-throughput.json) - Phase 3 metrics
4. Resource metrics (.resource-metrics.json) - Process resource usage
5. Container metrics (rabbitmq, postgres) - Infrastructure
6. System metrics (.system-metrics.json) - Overall system health
7. Chart (.chart.png) - Visual representation

---

## Important Notes for .NET Implementation

1. **Timestamps Must Match:** Use ISO 8601 format for samples, YYYY-MM-DD HH:MM:SS for main report
2. **Decimal Precision:** Strictly adhere to the precision rules above
3. **Zero Handling:** Apply the same logic as Python for min/max calculations
4. **Statistical Calculations:** Use the same formulas (sample standard deviation, not population)
5. **File Names:** Exact match of pattern test-report-{timestamp}-{process_name}.{suffix}
6. **JSON Structure:** Identical property names and nesting
7. **Mode Calculation:** Round float values to nearest integer before finding most common value
8. **Sampling:** All files use the same timestamp format (ISO 8601 with timezone)

