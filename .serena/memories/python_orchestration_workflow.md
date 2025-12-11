# Python Service Tester Orchestration Workflow

Complete analysis of the Python performance testing framework that needs to be replicated in .NET using BackgroundService and IHost patterns.

## Executive Summary

The Python service-tester implements a sophisticated multi-phase testing orchestration that:
1. **Detects and monitors** a running service on a specified port
2. **Warms up** the JIT compiler and connection pools (not measured)
3. **Publishes events** to RabbitMQ at maximum speed
4. **Consumes events** concurrently while tracking throughput
5. **Calls the HTTP API** under load using k6
6. **Monitors resource usage** in background threads
7. **Aggregates metrics** and generates comprehensive reports

---

## Phase 0: Warmup (Lines 477-570)

### Purpose
- **Goal**: Achieve steady-state performance before measuring
- **Why**: JIT compilers need time to optimize, connection pools need initialization
- **Metrics**: Warmup results are NOT included in final report

### Sequence
```
1. Create RabbitMQ connection (warmup_connection)
2. Start warmup_consumer_thread (separate thread)
   - Thread target: consume_events(test_state)
   - Daemon thread: True (terminates with main program)
3. Sleep 1 second (CONSUMER_SETUP_DELAY_SEC)
4. Publish WARMUP_EVENTS (200 events by default)
5. Close warmup_connection
6. Wait for all warmup events to be consumed
   - Timeout: 30 seconds
   - Poll interval: 100ms
   - Polls: test_state.received_event_count >= WARMUP_EVENTS
7. Run warmup API load test
   - Duration: 5 seconds (WARMUP_API_DURATION)
   - Virtual users: 1 (same as main test)
   - Uses k6 via run_k6_test()
8. Clear database (clean-service-data.sh)
9. Clear RabbitMQ queues (clear-rabbitmq.sh)
10. Reset test_state counters:
    - received_event_count = 0
    - throughput_samples.clear()
    - api_throughput_samples.clear()
```

### Error Handling
- If warmup fails: Continue with test anyway (logs warning)
- Counters reset regardless of warmup success/failure

---

## Phase 1: Publishing Events (Lines 572-614)

### Sequence
```
1. Create RabbitMQ connection
2. Create channel (pika.BlockingConnection)
3. Declare exchange:
   - Type: topic
   - Name: 'referenceservice.comparison' (from env var)
   - Durable: True
4. Start consumer thread (same as warmup)
   - Thread target: consume_events(test_state)
   - Daemon: True
5. Sleep CONSUMER_SETUP_DELAY_SEC (1 second)
   - Purpose: Give consumer time to declare queue and bind to exchange
6. Record phase_timestamps["phase1_start"]
7. Call publish_events(channel)
   - Publishes NUM_EVENTS (10000 by default)
   - Using send_multiple_events(channel, count, delay=0, verbose=False)
   - Delay=0 means: publish as fast as possible
   - Returns: elapsed time
8. Record phase_timestamps["phase1_end"]
9. Close connection
```

### Publishing Details (send_multiple_events)
```
For each event (0 to count-1):
  1. Pick random device from DEVICE_IDS
  2. Create CloudEvents v1.0 format event
  3. Send via channel.basic_publish():
     - Exchange: EXCHANGE_NAME
     - Routing key: EVENT_TYPE ('instrument.status.changed')
     - Body: JSON-encoded event
     - Properties: content_type='application/json', delivery_mode=2 (persistent)
  4. If delay > 0: sleep(delay)  [always 0 in tester, so no delay]

Event Format (CloudEvents v1.0):
{
  "id": uuid.uuid4(),
  "specversion": "1.0",
  "source": "urn:uuid:python-event-generator",
  "type": "instrument.status.changed",
  "time": datetime.now(utc).isoformat(),
  "datacontenttype": "application/json",
  "data": {
    "deviceId": device_id,
    "previousStatus": previous_status,
    "currentStatus": current_status
  }
}
```

### Error Handling
- If RabbitMQ unavailable: Log error and skip phase
- Consumer thread continues in background regardless

---

## Phase 2: Consuming Events (Lines 615-652)

### Concurrent Consumer Implementation

The consumer runs in a separate daemon thread throughout phases 1-2. Publisher and consumer run CONCURRENTLY.

```python
def consume_events(test_state: TestState) -> None:
    """Run in background thread - consumes events forever until NUM_EVENTS received"""
    
    # Create separate connection for this thread
    connection = pika.BlockingConnection(parameters)
    channel = connection.channel()
    
    # Declare exchange and queue (idempotent)
    channel.exchange_declare(exchange=EXCHANGE_NAME, exchange_type='topic', durable=True)
    channel.queue_declare(queue=CONSUMER_QUEUE, durable=True)
    channel.queue_bind(exchange=EXCHANGE_NAME, queue=CONSUMER_QUEUE, 
                       routing_key=CONSUME_EVENT_TYPE)
    
    # Set prefetch count (standardized across all services)
    channel.basic_qos(prefetch_count=RABBITMQ_PREFETCH_COUNT)  # Default: 100
    
    # Register callback handler
    channel.basic_consume(queue=CONSUMER_QUEUE, on_message_callback=callback)
    
    # Block until stop_consuming() called
    channel.start_consuming()
```

### Callback Handler (runs for each received message)

```python
def callback(ch, method, properties, body):
    # Thread-safe counter increment
    with test_state.received_lock:
        test_state.received_event_count += 1
        count = test_state.received_event_count
    
    current_time = time.time()
    
    # Sampling: every 100ms, record throughput sample
    if current_time - last_sample_time >= SAMPLING_INTERVAL_SEC (0.1):
        events_in_period = count - last_sample_count
        elapsed_since_sample = current_time - last_sample_time
        events_per_sec = events_in_period / elapsed_since_sample
        
        test_state.throughput_samples.append({
            "timestamp": datetime.now().isoformat(),
            "elapsed_seconds": current_time - start_time,
            "total_events": count,
            "events_per_second": events_per_sec
        })
    
    # Console logging: every 1 second
    if (current_time - last_log_time >= 1.0) or count >= NUM_EVENTS:
        log(f"Consuming: {count}/{NUM_EVENTS} events ...")
    
    # Acknowledge message
    ch.basic_ack(delivery_tag=method.delivery_tag)
    
    # Stop when done
    if count >= NUM_EVENTS:
        ch.stop_consuming()
```

### Phase 2: Main Thread Wait Loop

```python
if publish_time > 0:
    record phase_timestamps["phase2_start"]
    consume_start_time = time.time()
    
    # Poll until all events consumed or timeout
    timeout_seconds = 120
    while elapsed < timeout_seconds:
        with test_state.received_lock:
            count = test_state.received_event_count
        
        if count >= NUM_EVENTS:
            break
        
        time.sleep(0.1)  # Poll interval
        elapsed = time.time() - consume_start_time
    
    consume_time = time.time() - consume_start_time
    record phase_timestamps["phase2_end"]
    
    # Wait for consumer thread to finish
    if consumer_thread:
        consumer_thread.join(timeout=2)
```

### Thread Safety
- **Lock**: test_state.received_lock (threading.Lock)
- **Protected data**: test_state.received_event_count
- **Pattern**: Callback holds lock briefly, main thread also holds lock when reading

---

## Phase 3: API Load Testing (Lines 654-670)

### Sequence
```
1. Record phase_timestamps["phase3_start"]
2. Call run_k6_test():
   - Script: api-load-test.js
   - URL: API_URL (http://localhost:{port}/kpi)
   - Duration: API_DURATION ('30s' by default)
   - Virtual users: API_CONCURRENT_WORKERS (1 by default)
   - Samples: test_state.api_throughput_samples (modified in place)
3. Record phase_timestamps["phase3_end"]
4. Returns: (elapsed_time, success_count, error_count)
```

### k6 Test Details

**k6 Command Construction**:
```bash
k6 run \
  --vus {virtual_users} \
  --duration {duration} \
  --env API_URL={api_url} \
  api-load-test.js
```

**Test Script (api-load-test.js)**:
```javascript
export let options = {
  duration: '30s',
  vus: 1,
  thresholds: {},  // No thresholds - analyze in Python
  summaryTrendStats: ['min', 'avg', 'med', 'max', 'p(90)', 'p(95)', 'p(99)']
};

export default function() {
  const apiUrl = __ENV.API_URL;
  const res = http.get(apiUrl);
  check(res, {
    'status is 2xx': (r) => r.status >= 200 && r.status < 300,
  });
}
```

### k6 Output Parsing (parse_k6_output)

k6 output is parsed to extract:

1. **Final Summary Statistics**:
   - `http_reqs`: Total number of requests
   - `checks_succeeded`: Number of successful checks (2xx responses)
   - Calculate: error_count = total_checks - success_count

2. **Progress Samples**:
   - Parse lines like: `running (01.0s), 1/1 VUs, 754 complete and 0 interrupted iterations`
   - Extract: elapsed_seconds and complete_iterations at each progress update
   - If no progress data (output redirected): Generate interpolated samples at 1-second intervals

3. **Throughput Samples**:
   - For each progress data point:
     ```python
     elapsed_sec, iterations = progress_data[i]
     calls_per_sec = (iterations - prev_iterations) / (elapsed_sec - prev_elapsed)
     
     api_throughput_samples.append({
         "timestamp": datetime.fromtimestamp(start_time + elapsed_sec).isoformat(),
         "elapsed_seconds": elapsed_sec,
         "total_calls": iterations,
         "calls_per_second": calls_per_sec
     })
     ```

---

## Monitoring Infrastructure

### Four Parallel Monitoring Threads

#### 1. ProcessMonitor (Lines 434-435)
- **Port**: Specified by CLI arg (default 8080)
- **Interval**: 500ms (DEFAULT_SERVICE_MONITORING_INTERVAL_MS)
- **Metrics per sample**:
  ```python
  {
      "timestamp": ISO timestamp,
      "cpu_percent": float,
      "memory_rss_mb": float,
      "threads": int
  }
  ```
- **Summary statistics**:
  - avg_cpu_percent, peak_cpu_percent
  - avg_memory_rss_mb, peak_memory_rss_mb
  - total_samples count

#### 2. ContainerMonitor: RabbitMQ (Lines 440-442)
- **Container**: performancetest-rabbitmq (from env var)
- **Interval**: 3000ms (CONTAINER_MONITORING_INTERVAL_MS)
- **Metrics**: CPU, Memory, I/O via `docker stats`

#### 3. ContainerMonitor: PostgreSQL (Lines 447-449)
- **Container**: performancetest-postgres (from env var)
- **Interval**: 3000ms
- **Metrics**: Same as RabbitMQ

#### 4. SystemMonitor (Lines 453-454)
- **Interval**: 500ms
- **Metrics**: System-wide CPU, memory, disk usage

### Timing of Monitoring
```
Start: Before Phase 0 warmup
Stop: After Phase 3 API testing
Wait: FINAL_MONITORING_DELAY_SEC (1.0 second) before stopping monitors

This ensures all metrics are captured including post-test stabilization
```

---

## Metrics Collection

### Event Throughput Samples (test_state.throughput_samples)
**When**: During Phase 2 consumption
**Frequency**: Every 100ms (SAMPLING_INTERVAL_MS)
**Data**:
```python
{
    "timestamp": ISO datetime,
    "elapsed_seconds": float (relative to consume start),
    "total_events": int (cumulative),
    "events_per_second": float (instantaneous)
}
```

### API Throughput Samples (test_state.api_throughput_samples)
**When**: During Phase 3 API testing
**Frequency**: Parsed from k6 progress output (typically every 1-5 seconds)
**Data**:
```python
{
    "timestamp": ISO datetime,
    "elapsed_seconds": float (relative to API test start),
    "total_calls": int (cumulative iterations),
    "calls_per_second": float (instantaneous)
}
```

### Summary Statistics (report_generator.py)
For both event and API throughput:
```python
{
    "avg_X_per_second": float,
    "peak_X_per_second": float,
    "min_X_per_second": float,
    "std_dev_X_per_second": float,
    "cv_X_per_second": float,  # Coefficient of Variation (%)
    "avg_response_time_ms": float,  # 1000 / avg_rate
    "total_samples": int,
    "total_X": int
}
```

---

## Environment Variables & Configuration

### RabbitMQ Configuration
- `RABBITMQ_HOST` (default: localhost)
- `RABBITMQ_PORT` (default: 5672)
- `RABBITMQ_USER` (default: admin)
- `RABBITMQ_PASS` (default: admin)
- `RABBITMQ_EXCHANGE` (default: referenceservice.comparison)
- `RABBITMQ_EVENT_TYPE` (default: instrument.status.changed)
- `RABBITMQ_CONSUME_EVENT_TYPE` (default: instrumentstatus.kpi.updated)
- `RABBITMQ_CONSUMER_QUEUE` (default: service-tester)
- `RABBITMQ_PREFETCH_COUNT` (default: 100)
  - **Note**: Bun uses prefetch 1 due to framework limitations
  - **Standardized**: All other services use 100

### Testing Configuration
- `NUM_EVENTS` (default: 10000) - Events to publish/consume
- `API_DURATION` (default: 30s) - API load test duration
- `API_CONCURRENT_WORKERS` (default: 1) - k6 virtual users
- `API_PORT` (default: 8080) - Service port to test
- `WARMUP_EVENTS` (default: 200)
- `WARMUP_API_DURATION` (default: 5s)

### Container Names
- `RABBITMQ_CONTAINER_NAME` (default: performancetest-rabbitmq)
- `POSTGRES_CONTAINER_NAME` (default: performancetest-postgres)

### Monitoring Intervals
- Service monitoring: 500ms
- Container monitoring: 3000ms (3x slower to reduce overhead)
- Throughput sampling: 100ms
- Final monitoring wait: 1.0s

---

## CLI Arguments

```
--events NUM (default: 10000)
  Number of events to publish and consume

--api-duration TIME (default: 30s)
  Duration for API load testing (format: 10s, 5m, 2h)

--api-workers NUM (default: 1)
  Number of concurrent virtual users for k6

--port PORT (default: 8080)
  Port where the service is running

--results-folder PATH (default: ./test-results)
  Folder to save test results and reports
```

---

## Output Files Generated

### Format: `test-report-{YYYYMMDD_HHMMSS}-{program_name}.{suffix}`

**Example**: `test-report-20251022_122306-goReferenceService.json`

### Files Created:
1. **{}.json** - Main test report
   - Configuration, results, phase timestamps
   - Success/error counts, throughput summary

2. **{}-resource-metrics.json** - Process metrics
   - CPU, memory, thread count samples
   - Summary statistics

3. **{}-events-throughput.json** - Event processing metrics
   - Throughput samples (100ms intervals)
   - Summary statistics (avg, peak, min, std dev, CV)

4. **{}-api-throughput.json** - API load test metrics
   - Throughput samples from k6
   - Summary statistics

5. **{}-rabbitmq-metrics.json** - RabbitMQ container metrics
   - CPU, memory, I/O over time
   - Summary statistics

6. **{}-postgres-metrics.json** - PostgreSQL container metrics
   - CPU, memory, I/O over time
   - Summary statistics

7. **{}-system-metrics.json** - System-wide metrics
   - Total CPU, memory, disk usage
   - Summary statistics

8. **{}-chart.png** - Visualization
   - 3 subplots: Event throughput, API throughput, Resource usage
   - Timeline shows warmup vs measured phases

---

## Error Handling Strategy

### Database/RabbitMQ Cleanup
- **Retry**: 2 attempts with 2-second delay between retries
- **Timeout**: 30 seconds for database cleanup, 10 seconds for RabbitMQ
- **Failure**: Raises RuntimeError after all retries exhausted

### Service Detection
- **Timeout**: 30 seconds to detect service on port
- **Method 1**: Use `lsof -ti :{port}` (fast)
- **Method 2**: Fallback to psutil net_connections
- **User interaction**: If not found, prompts user to start service and allows retry

### Consumer Thread
- **Join timeout**: 2 seconds after test phase ends
- **Daemon**: True (will be terminated by main program if needed)

### k6 Execution
- **Timeout**: API_DURATION + 30 seconds
- **Failure**: Returns (0, 0, 0) - all metrics zero
- **No k6**: Returns (0, 0, 0) gracefully

### Warmup Phase
- **Timeout**: 30 seconds for event consumption
- **Failure**: Logs warning, continues with test
- **Counters**: Reset regardless of success/failure

---

## Key Design Patterns

### 1. Thread-Safe State Management
```python
@dataclass
class TestState:
    received_event_count: int = 0
    received_lock: threading.Lock = field(default_factory=threading.Lock)
    throughput_samples: list[dict] = field(default_factory=list)
    api_throughput_samples: list[dict] = field(default_factory=list)
```
- Single lock protects counter
- Lists (throughput_samples) appended safely from single consumer thread

### 2. Concurrent Publisher/Consumer
- Publisher: Runs in main thread, publishes as fast as possible
- Consumer: Runs in daemon thread, processes in callback
- Synchronization: Consumer thread's callback and main thread's counter reads share lock

### 3. Phase Isolation
- Warmup: Complete cleanup of counters and samples before Phase 1
- Phase 1 & 2: Run concurrently (publish while consuming)
- Phase 3: Starts after Phase 2 completes

### 4. Metric Collection Strategy
- **Process metrics**: Background thread, continuous sampling
- **Event throughput**: Callback-driven (real-time per event)
- **API throughput**: Parser-driven (from k6 output)
- **All metrics**: Timestamped with ISO format, include elapsed time and totals

---

## Timing Constants Summary

| Constant | Value | Purpose |
|----------|-------|---------|
| WARMUP_EVENTS | 200 | Events for JIT warmup |
| WARMUP_API_DURATION | 5s | API warmup duration |
| NUM_EVENTS | 10000 | Main test event count |
| API_DURATION | 30s | Main test API duration |
| SAMPLING_INTERVAL_MS | 100 | Throughput sampling frequency |
| CONSUMER_SETUP_DELAY_SEC | 1 | Wait for consumer thread setup |
| FINAL_MONITORING_DELAY_SEC | 1.0 | Wait for final monitoring samples |
| RABBITMQ_PREFETCH_COUNT | 100 | Standardized prefetch (except Bun=1) |
| CONTAINER_MONITORING_INTERVAL_MS | 3000 | 3x slower than service monitoring |
| DEFAULT_SERVICE_MONITORING_INTERVAL_MS | 500 | Service process monitoring frequency |

---

## Replication in .NET

### Key Components to Implement:
1. **IHost + BackgroundService** for concurrent phases
2. **Channel<T>** for thread-safe event queues
3. **IBackgroundTaskQueue** for monitoring tasks
4. **HttpClient** for API testing (instead of k6)
5. **EasyNetQ/MassTransit** for RabbitMQ
6. **Process/PerformanceCounter** for system monitoring
7. **Task-based concurrency** instead of threading.Thread

### Architecture Pattern:
```
Main Program
├── Initialize IHost
├── Configure Dependency Injection
├── Start Background Services:
│   ├── ProcessMonitorService (continuous)
│   ├── ContainerMonitorService (continuous)
│   ├── TestOrchestratorService (phases)
│   │   ├── Phase 0 (Warmup)
│   │   ├── Phase 1 (Publish) - concurrent with Phase 2
│   │   ├── Phase 2 (Consume)
│   │   └── Phase 3 (API Load)
│   └── ReportGeneratorService (final)
└── WaitForCompletionAsync()
```

### Concurrency Model:
- Use `Channel<Event>` for event communication
- Use `Task.Run()` for background operations
- Use `ConcurrentBag<T>` for thread-safe sample collection
- Use `TaskCompletionSource<T>` for signal waiting
