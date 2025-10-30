# Performance Tester

A collection of tools for testing and benchmarking service performance, focusing on event-driven architectures with RabbitMQ and HTTP APIs.

## Overview

This toolkit allows you to:

- Publish events to RabbitMQ
- Consume and measure event throughput
- Test HTTP API performance
- Monitor CPU and memory usage of services
- Generate comprehensive performance reports with metrics and charts

## Prerequisites

- Python 3.13+ (minimum 3.9, but 3.13+ recommended for performance)
  - **Why 3.13+?** The code uses modern type annotations (PEP 585) like `dict[str, Any]` which require Python 3.9+. Python 3.13 provides significant performance improvements (~10-20% faster) and better developer experience.
- RabbitMQ running (typically via Docker)
- PostgreSQL database (for performance test scenarios)
- **k6** - Modern load testing tool (required for API testing)
- Your service running on a port (default: 8080, configurable with `--port`)
- Required Python packages:
  - `pika` - RabbitMQ client
  - `psutil` - System and process utilities
  - `matplotlib` - Chart generation

Install dependencies:

```bash
# Python packages (use python3.13 if available, otherwise python3.9+)
python3.13 -m pip install -r requirements.txt
# Or with python3 if you have 3.13+ as default
pip3 install -r requirements.txt

# k6 (required for API testing)
sudo snap install k6
# Or visit: https://k6.io/docs/get-started/installation/
```

**Note:** If you used the `setup-environment.sh` script from the root directory, these dependencies are already installed.

## Required Files

The performance-tester directory should contain:

- `service-tester.py` - Main test orchestrator
- `send_events.py` - Event publisher (imported by service-tester)
- `api-load-test.js` - k6 script for API load testing
- `compare_test_results.py` - Tool for comparing multiple test results
- `clean-service-data.sh` - Database cleanup script (clears all tables dynamically)
- `clear-rabbitmq.sh` - RabbitMQ queue cleanup script
- `service-tester.single-file.py` - Single-file bundled version (alternative distribution)

**Supporting modules** (imported by service-tester.py):
- `system_info.py`, `process_monitor.py`, `container_monitor.py`, `system_monitor.py`
- `report_generator.py`, `chart_generator.py`, `k6_runner.py`

## Tools

### 1. `service-tester.py`

The main performance testing tool that orchestrates comprehensive service testing.

**Features:**

- Automatically waits for service to be ready on specified port (default: 8080)
- Clears database and RabbitMQ queues before testing
- Starts consumer thread to receive events of type `instrumentstatus.kpi.updated`
- Publishes events to RabbitMQ (type: `instrument.status.changed`)
- Measures event consumption throughput
- Uses **k6** to push maximum sustained load to HTTP API for specified duration
- Monitors CPU and memory usage of target process (sampling every 500ms)
- Generates detailed reports with charts and real-time progress

**Usage:**

```bash
# Run with defaults (10000 events, 30s API test, 1 concurrent worker)
python service-tester.py

# Custom configuration
python service-tester.py --events 5000 --api-duration 2m --api-workers 100

# Short burst test
python service-tester.py --events 1000 --api-duration 10s --api-workers 100

# Long-running stress test
python service-tester.py --events 20000 --api-duration 5m --api-workers 200

# Extended endurance test with custom results folder
python service-tester.py --api-duration 1h --results-folder ./my-test-results
```

**Options:**

- `--port N` - Port where the service is running (default: 8080)
- `--events N` - Number of events to publish/consume (default: 10000)
- `--api-duration TIME` - Duration for API load testing (format: `10s`, `5m`, `2h`). Default: `30s`
- `--api-workers N` - Number of concurrent virtual users for k6 (default: 1)
- `--results-folder PATH` - Custom folder for test results (default: `./test-results`)

**API Testing with k6:**

The tester uses `k6` (a modern open-source load testing tool) for API testing with real-time progress display. By default, the test runs with **1 concurrent virtual user** to provide fair single-threaded framework comparisons. You can increase `--api-workers` to test parallel performance (e.g., `--api-workers 50` can achieve 10,000-15,000+ req/s depending on your service's capacity).

**Duration format:**
- Seconds: `10s`, `30s`, `90s`
- Minutes: `1m`, `5m`, `10m`
- Hours: `1h`, `2h`, `24h`

**Test Phases:**

1. **Setup** - Waits for service on specified port, clears database and RabbitMQ queues
2. **Phase 0: Warmup** - Sends 200 warmup events + 5s of API warmup to stabilize the service before actual testing
3. **Phase 1: Publish** - Consumer thread starts first, then sends events to RabbitMQ as fast as possible
4. **Phase 2: Consume** - Waits for consumer thread to receive and process all events from queue
5. **Phase 3: API** - Uses k6 to push maximum load with HTTP GET requests to `/kpi` endpoint for specified duration

**Output:**

All files are saved to the results folder (default: `test-results/`) with a common timestamp and program name:

- **Console logs** - Real-time progress with timestamps (UTC)
- **`test-report-{timestamp}-{programname}.json`** - Main summary report containing:
  - Test date and total runtime
  - System information (OS, CPU, RAM, disk)
  - Monitored process details (name, PID, port)
  - Test configuration (events count, API duration, workers)
  - Phase timestamps and results for publish, consume, and API tests

- **`test-report-{timestamp}-{programname}.resource-metrics.json`** - Process monitoring data:
  - CPU percentage samples every 500ms
  - Memory (RSS and VMS) in MB
  - Thread count
  - Summary statistics (avg, peak)

- **`test-report-{timestamp}-{programname}.events-throughput.json`** - Event consumption metrics:
  - Events per second samples every 500ms
  - Cumulative event counts
  - Summary statistics (avg, peak, min, std dev, CV%)

- **`test-report-{timestamp}-{programname}.api-throughput.json`** - API call metrics:
  - Calls per second samples every 500ms
  - Cumulative call counts
  - Summary statistics (avg, peak, min, std dev, CV%)

- **`test-report-{timestamp}-{programname}.postgres-metrics.json`** - PostgreSQL container metrics

- **`test-report-{timestamp}-{programname}.rabbitmq-metrics.json`** - RabbitMQ container metrics

- **`test-report-{timestamp}-{programname}.system-metrics.json`** - System-wide resource metrics

- **`test-report-{timestamp}-{programname}.chart.png`** - Combined visualization (3 subplots):
  - Throughput chart (events/sec and API calls/sec)
  - CPU usage chart
  - Memory usage chart
  - Phase boundary markers and labels

### 2. `send_events.py`

Standalone script for publishing events to RabbitMQ.

**Features:**

- Generates CloudEvents-compliant instrument status events
- Supports batch event sending
- Configurable delays between events

**Usage:**

```bash
# Send 100 events as fast as possible
python send_events.py --count 100

# Send events with 1 second delay between each
python send_events.py --count 50 --delay 1.0

# Send single event
python send_events.py
```

**Options:**

- `--count N` - Number of events to send (default: 1)
- `--delay N` - Delay between events in seconds (default: 0)

**Event Structure:**

- Type: `instrument.status.changed`
- Format: CloudEvents v1.0 with custom extensions (`privacyrelevant`, `kind`)
- Contains: `deviceId`, `previousStatus`, `currentStatus`
- Device IDs: DEVICE-001 through DEVICE-005
- Statuses: IDLE, RUNNING, ERROR, MAINTENANCE, OFFLINE

### 3. `clean-service-data.sh`

Clears all data from a specific service's PostgreSQL database.

**Usage:**

```bash
./clean-service-data.sh <database_name>

# Examples:
./clean-service-data.sh go_db
./clean-service-data.sh dotnet9_db
./clean-service-data.sh python_db
```

**What it does:**

- Connects to PostgreSQL container
- Dynamically discovers all tables in the specified database
- Truncates all tables (not just `instrument_status`)
- Verifies the database is empty

### 4. `clear-rabbitmq.sh`

Purges all messages from all RabbitMQ queues.

**Usage:**

```bash
./clear-rabbitmq.sh
```

**What it does:**

- Lists all queues
- Purges each queue individually
- Reports success/failure for each queue

### 5. `api-load-test.js`

k6 JavaScript script for API load testing.

**Purpose:**

- Configurable via environment variables (`API_URL`, `VUS`, `DURATION`)
- Makes HTTP GET requests to the API endpoint
- Validates response status (expects 2xx)
- Checks for valid JSON response body
- Reports real-time progress and metrics

**Metrics tracked:**

- HTTP request duration and success rate
- Checks passed/failed (status code and response validation)
- Detailed latency percentiles and statistics
- Request throughput over time

This script is automatically invoked by service-tester.py when running API tests.

### 6. `compare_test_results.py`

Compares multiple test results and generates comprehensive markdown comparison reports.

**Features:**

- Discovers and groups test result files by timestamp
- Compares throughput metrics (events and API calls)
- Compares resource usage (CPU, memory, system-wide)
- Highlights best/worst performers with bold formatting
- Identifies fastest, most stable, and most efficient test runs
- Generates timestamped markdown reports

**Usage:**

```bash
# Compare all results in default folder (saves to test-results/)
python compare_test_results.py

# Compare results in specific folder
python compare_test_results.py --folder /path/to/results

# Print report to stdout instead of file
python compare_test_results.py --stdout

# Save report to custom location
python compare_test_results.py --output /path/to/report.md
```

**Options:**

- `--folder PATH` - Folder containing test result JSON files (default: `./test-results`)
- `--output PATH` - Custom output file path for the report
- `--stdout` - Print report to stdout instead of saving to file

**Output:**

The tool generates a markdown report (default: `test-report-comparison-{timestamp}.md`) containing:

- **Test Runs Overview** - Summary table of all test runs with configuration
- **Throughput Comparison** - Event processing and API throughput metrics
- **Resource Usage Comparison** - Process CPU, memory, and system-wide metrics
- **Performance Highlights** - Best performers identified:
  - Fastest event processing
  - Fastest API throughput
  - Most stable event processing (lowest CV%)
  - Most stable API throughput (lowest CV%)
  - Lowest CPU usage
  - Lowest memory usage

**Notes:**

- Automatically discovers all test result files with matching timestamps
- Bold formatting highlights best values in each category
- Sorts test runs by timestamp (newest first)
- Useful for comparing different implementations, configurations, or optimization attempts

## Configuration

Default configuration in the scripts:

**RabbitMQ:**

- Host: `localhost`
- Port: `5672`
- User: `admin`
- Password: `admin`
- Exchange: `referenceservice.comparison`

**API:**

- URL: `http://localhost:{PORT}/kpi` (port specified via `--port`, default: 8080)

**Monitoring:**

- Target port: Configurable via `--port` (default: 8080)
- Sampling interval: 500ms
- Default events: 10000
- Default API duration: 30s
- Default API workers: 1
- Consumer queue: `service-tester`
- Consumer event type: `instrumentstatus.kpi.updated`

## Workflow Example

### Complete Performance Test

```bash
# 1. Start your services (RabbitMQ, PostgreSQL, application)
docker-compose up -d

# 2. Run performance test (specify port if not 8080)
python service-tester.py --port 8094 --events 5000 --api-duration 1m --api-workers 100

# 3. Review results in test-results/ directory
```

### Comparing Multiple Test Runs

```bash
# Run tests with different configurations
python service-tester.py --events 10000 --api-duration 1m --api-workers 50
python service-tester.py --events 10000 --api-duration 1m --api-workers 100

# Generate comparison report
python compare_test_results.py
```

### Send Events Only

```bash
# Send a large batch of events to stress test
python send_events.py --count 10000
```

## Test Results

All test results are saved to the `test-results/` directory with timestamped filenames.

**Report Contents:**

- System information (OS, CPU, memory)
- Test parameters (event count, API calls, etc.)
- Performance metrics (throughput, latency)
- Resource usage (CPU%, memory MB)
- Timing breakdown for each phase
- Generated charts showing metrics over time

**Chart Visualization:**

The generated PNG chart displays three subplots with synchronized timestamps:

1. **Throughput (top)** - Combined view showing both:
   - Consumed events per second (green)
   - API calls per second (orange)
   - Average, min, max, mode, std dev, and CV% for each
   - Phase labels indicating which test phase is active

2. **CPU Usage (middle)** - Process CPU utilization:
   - CPU percentage over time (blue)
   - Average, min, max, and mode values

3. **Memory Usage (bottom)** - Process memory consumption:
   - RSS memory in MB (red)
   - Average, min, max, and mode values

Each chart includes phase boundary markers showing when publishing, consuming, and API testing phases occur.

## Understanding Variability Metrics

The performance tester calculates **variability metrics** to help you understand the stability and consistency of your service's throughput. These metrics answer the question: *"Is my service consistently processing at the average rate, or does it work in bursts?"*

### Standard Deviation (Std Dev)

**What it measures:** The absolute spread of throughput values from the average.

- **Low Std Dev** (e.g., 5-15 events/s) = Stable, consistent throughput
- **High Std Dev** (e.g., 50+ events/s) = Highly variable, bursty behavior

**Example:**
- Service A: Avg = 200 events/s, Std Dev = 10 events/s → Most samples are 190-210 events/s
- Service B: Avg = 200 events/s, Std Dev = 60 events/s → Samples range widely (140-260 events/s)

### Coefficient of Variation (CV%)

**What it measures:** The relative variability as a percentage (Std Dev / Mean × 100). This normalizes variability across different scales, making it easier to compare services with different average throughput.

**Interpretation Guidelines:**
- **CV < 10%**: Very stable/consistent performance ✅
  - *Example: Avg = 300/s, rates vary between 290-310/s*
- **CV 10-20%**: Moderate variability ⚠️
  - *Example: Avg = 300/s, rates vary between 250-350/s*
- **CV > 20%**: High variability/bursty behavior 🚨
  - *Example: Avg = 300/s, rates fluctuate between 100-500/s*

### Why This Matters

Two services can have the same average throughput but behave very differently:

**Scenario 1: Stable Service**
```
Average: 300 events/s
Std Dev: 15 events/s
CV: 5%
→ Predictable, consistent processing. Suitable for real-time workloads.
```

**Scenario 2: Bursty Service**
```
Average: 300 events/s
Std Dev: 80 events/s
CV: 27%
→ Unpredictable bursts. May cause latency spikes or queue buildup.
```

### Where to Find These Metrics

1. **Console Output** - Displayed during test runs:
   ```
   Throughput metrics saved to: ...
     Avg: 198.78 events/s
     Peak: 263.08 events/s
     Min: 0.66 events/s
     Std Dev: 45.23 events/s
     CV: 22.76% (variability)
   ```

2. **JSON Reports** - In `*.events-throughput.json` and `*.api-throughput.json`:
   ```json
   "summary": {
     "avg_events_per_second": 198.78,
     "peak_events_per_second": 263.08,
     "min_events_per_second": 0.66,
     "std_dev_events_per_second": 45.23,
     "cv_events_per_second": 22.76
   }
   ```

3. **Chart Legends** - Displayed in the throughput chart PNG:
   ```
   Events Avg: 198.8
   Min: 0.7
   Max: 263.1
   Mode: 210
   Std Dev: 45.2
   CV: 22.8%
   ```

## Troubleshooting

**No process found on port:**

- Verify your service is running: `netstat -an | grep <PORT>` (Linux) or `netstat -an | findstr <PORT>` (Windows)
- Ensure you're using the correct `--port` argument matching your service
- The tester will wait up to 30 seconds for the service to start

**RabbitMQ connection failed:**

- Ensure RabbitMQ container is running: `docker ps | grep rabbitmq`
- Verify credentials in the scripts match your RabbitMQ configuration
- Check that exchange `referenceservice.comparison` is declared

**Database clear fails:**

- Check PostgreSQL container: `docker ps | grep postgres`
- Verify database and table exist
- Ensure `clean-service-data.sh` script has execute permissions
- Remember to provide database name as argument: `./clean-service-data.sh <db_name>`

**Consumer timeout:**

- If you see "Only received X/Y events after 60s timeout", your service may not be processing events
- Check service logs for errors
- Verify the service is consuming from the correct queue and transforming events properly
- The consumer expects events of type `instrumentstatus.kpi.updated`

**k6 not found:**

- Install k6: `sudo snap install k6` or visit https://k6.io/docs/get-started/installation/
- Verify installation: `which k6`

**Missing api-load-test.js:**

- Ensure `api-load-test.js` is in the same directory as `service-tester.py`
- This file is required for API testing

## Notes

- The service-tester requires a running service on the specified port (default: 8080) before starting
- Database and RabbitMQ queues are automatically cleared before each test
- Consumer thread starts before publishing to ensure no events are lost
- Publisher sends events of type `instrument.status.changed`
- Consumer receives events of type `instrumentstatus.kpi.updated` (transformed by service)
- Tests run in three sequential phases: publish → consume → API
- Process monitoring samples every 500ms during entire test run
- API testing uses k6 to push maximum sustained load for specified duration with real-time progress
- Events use CloudEvents v1.0 format with custom extensions (`privacyrelevant` and `kind`)
- All timestamps are in UTC
- Results are cumulative - old test results are preserved with unique timestamps
- The k6 script (`api-load-test.js`) must be present in the same directory
