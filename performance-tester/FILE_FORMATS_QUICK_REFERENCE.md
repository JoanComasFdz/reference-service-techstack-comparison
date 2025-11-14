# File Formats Quick Reference

## File Naming Pattern
```
test-report-{YYYYMMDD_HHMMSS}-{process_name}.{suffix}
```

Example: `test-report-20251113_142530-dotnet9aotreferenceservice.json`

## All File Types Generated

| Suffix | Purpose | Content Type |
|--------|---------|--------------|
| `.json` | Main test report | JSON - Summary of all phases |
| `.resource-metrics.json` | Process monitoring | JSON - CPU & RAM samples |
| `.api-throughput.json` | API load test metrics | JSON - API call throughput |
| `.events-throughput.json` | Event processing metrics | JSON - Event throughput |
| `.system-metrics.json` | Overall system health | JSON - System CPU & RAM |
| `.rabbitmq-metrics.json` | RabbitMQ container metrics | JSON - Container monitoring |
| `.postgres-metrics.json` | PostgreSQL container metrics | JSON - Container monitoring |
| `.chart.png` | Performance visualization | PNG - 5-subplot chart |

## Decimal Precision by Field Type

```
Time in seconds:        3 decimals (0.123)
Throughput (per sec):   2 decimals (254.98)
CPU Percent:            2 decimals (25.50)
Memory (MB/GB):         2 decimals (125.75)
Percentages:            1 decimal  (100.0)
Response Time (ms):     3 decimals (9.325)
```

## Timestamp Formats

**Main Report:**
- Format: `YYYY-MM-DD HH:MM:SS`
- Example: `2025-11-13 14:25:30`

**Sample Timestamps:**
- Format: `YYYY-MM-DDTHH:MM:SS.ffffff+00:00` (ISO 8601)
- Example: `2025-11-13T14:25:30.100000+00:00`

## Key Formulas

**Coefficient of Variation:**
```
CV% = (standard_deviation / mean) * 100
```

**Average Response Time:**
```
response_time_ms = 1000.0 / throughput_per_second
```

**Standard Deviation:**
```
Use sample standard deviation (n-1), not population
```

## Main Report Structure

```
{
  "test_date": "YYYY-MM-DD HH:MM:SS",
  "total_runtime_seconds": float (3 decimals),
  "system": { system_info_object },
  "phase_timestamps": { optional timestamps },
  "monitored_process": { optional process info },
  "configuration": { test config },
  "results": {
    "phase1_publish": { duration_seconds, throughput_events_per_sec },
    "phase2_consume": { duration_seconds, throughput_events_per_sec },
    "phase3_api": { duration_seconds, total_requests, throughput_calls_per_sec, 
                    success_count, success_percentage, error_count, error_percentage }
  }
}
```

## Throughput Report Structure (events and API)

```
{
  "test_date": "YYYY-MM-DD HH:MM:SS",
  "sampling_interval_ms": 100,
  "samples": [
    {
      "timestamp": "ISO-8601",
      "elapsed_seconds": float (3 decimals),
      "total_events": int,  // OR total_calls for API
      "events_per_second": float (2 decimals)  // OR calls_per_second
    }
  ],
  "summary": {
    "avg_events_per_second": float (2 decimals),
    "peak_events_per_second": float (2 decimals),
    "min_events_per_second": float (2 decimals),  // zeros excluded
    "std_dev_events_per_second": float (2 decimals),
    "cv_events_per_second": float (2 decimals),
    "avg_response_time_ms": float (3 decimals),
    "total_samples": int,
    "total_events": int
  }
}
```

## Resource Metrics Structure

```
{
  "test_date": "YYYY-MM-DD HH:MM:SS",
  "process_info": { pid, name, port },
  "sampling_interval_ms": 500,
  "samples": [
    {
      "timestamp": "ISO-8601",
      "cpu_percent": float (2 decimals),
      "memory_rss_mb": float (2 decimals),
      "threads": int
    }
  ],
  "summary": {
    "avg_cpu_percent": float (2 decimals),
    "peak_cpu_percent": float (2 decimals),
    "avg_memory_rss_mb": float (2 decimals),
    "peak_memory_rss_mb": float (2 decimals),
    "total_samples": int
  }
}
```

## Container Metrics Structure

```
{
  "test_date": "YYYY-MM-DD HH:MM:SS",
  "container_info": { name, id },
  "sampling_interval_ms": 3000,
  "samples": [
    {
      "timestamp": "ISO-8601",
      "cpu_percent": float (2 decimals),
      "memory_mb": float (2 decimals)  // NOT memory_rss_mb
    }
  ],
  "summary": {
    "avg_cpu_percent": float (2 decimals),
    "peak_cpu_percent": float (2 decimals),
    "avg_memory_mb": float (2 decimals),
    "peak_memory_mb": float (2 decimals),
    "total_samples": int
  }
}
```

## System Metrics Structure

```
{
  "test_date": "YYYY-MM-DD HH:MM:SS",
  "cpu_count": int,
  "sampling_interval_ms": 500,
  "is_wsl2": true,  // optional, only if true
  "windows_host_total_ram_mb": float (2 decimals),  // optional, WSL2 only
  "samples": [
    {
      "timestamp": "ISO-8601",
      "cpu_percent": float (2 decimals),
      "memory_used_mb": float (2 decimals),
      "memory_total_mb": float (2 decimals),
      "memory_percent": float (2 decimals)
    }
  ],
  "summary": {
    "avg_cpu_percent": float (2 decimals),
    "peak_cpu_percent": float (2 decimals),
    "min_cpu_percent": float (2 decimals),
    "avg_memory_used_mb": float (2 decimals),
    "peak_memory_used_mb": float (2 decimals),
    "avg_memory_percent": float (2 decimals),
    "peak_memory_percent": float (2 decimals),
    "total_samples": int
  }
}
```

## System Information Object

```
{
  "os": "Linux|Windows",
  "os_release": string,
  "os_version": string,
  "wsl_version": "WSL1|WSL2",  // optional
  "cpu": {
    "model": string,
    "logical_processors": int,
    "physical_processors": int,
    "speed_mhz": float (2 decimals)
  },
  "ram": {
    "total_gb": float (2 decimals),
    "speed": string,
    "type": string,
    "manufacturer": string  // optional
  },
  "disks": [
    {
      "name": string,
      "size": string,  // "2.0T", "500.0G"
      "type": string,  // "SSD", "HDD", "SSD (NVMe)", etc
      "model": string  // optional
    }
  ]
}
```

## JSON Formatting Rules

- **Indentation:** 2 spaces
- **Encoding:** UTF-8
- **Decimals:** Use precise decimal places (not rounded to integers)
- **Null/None:** Use `null` in JSON
- **Booleans:** Use `true`/`false` (lowercase)
- **Numbers:** No quotes; use decimal points for floats
- **Trailing Commas:** None (standard JSON)

## Process Name Sanitization

```python
# Algorithm:
1. Remove extensions (.jar, .dll, .exe)
2. Replace [^\\w\\-\\.]+ with '-'
3. Replace -+ with -
4. Strip leading/trailing -
5. Convert to lowercase

# Examples:
"pythonReferenceService" → "pythonreferenceservice"
"dotnet9AotReferenceService" → "dotnet9aotreferenceservice"
"java21SpringBootGraalReferenceService" → "java21springbootgraalreferenceservice"
```

## Min Value Handling (Throughput)

- **For min calculation:** Exclude zero values (post-test measurements)
- **For average calculation:** Include all values including zeros
- **Rationale:** Provides meaningful minimum performance while keeping accurate averages

## Zero Handling Rules

| Context | Handling | Reason |
|---------|----------|--------|
| Throughput min | Exclude zeros | Zeros are post-test idle measurements |
| Throughput avg | Include zeros | Represents actual average over full period |
| CPU samples | Include all | System may be idle at times |
| Memory samples | Include all | Memory may not change much |
| Timestamps | Never zero | All should be valid times |

## Key Sampling Intervals

| Source | Interval | Notes |
|--------|----------|-------|
| Process monitoring | 500ms | Resource usage sampling |
| System monitoring | 500ms | System-wide CPU/memory |
| Container monitoring | 3000ms | Slower to reduce docker stats overhead |
| Throughput tracking | 100ms | Event/API throughput |

## Chart File Details

- **Format:** PNG, 150 DPI
- **Size:** Approximately 14x16 inches
- **Subplots:** 5 (vertically stacked)
- **Title:** "Performance Metrics - {process} - {Date} at {Time}"
- **Shared X-axis:** Time (HH:MM:SS format)
- **Phase Lines:** Vertical lines marking phase boundaries

## Standard Configuration Values

```
num_events: 10000 (typically)
api_duration: "30s" (k6 format)
api_concurrent_workers: 1 (single worker baseline)
rabbitmq_exchange: "instrument"
consumer_queue: "service-tester"
api_endpoint: "http://localhost:{port}/kpi"
publish_event_type: "instrument.status.changed"
consume_event_type: "instrumentstatus.kpi.updated"
```

## Important Notes

1. All timestamps in JSON must be ISO 8601 or YYYY-MM-DD HH:MM:SS
2. All numeric values must be actual numbers, not strings
3. CSV or other formats should match Python output exactly
4. Mode is calculated by rounding floats to nearest integer first
5. Standard deviation uses sample formula (n-1), not population (n)
6. Only disks >= 500GB are included in system info
7. Process names are sanitized to lowercase with dashes
