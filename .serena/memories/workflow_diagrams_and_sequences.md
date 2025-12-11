# Workflow Diagrams and Sequences

Detailed visual representations of the orchestration workflow, data flows, and state transitions.

---

## Main Test Flow

```
START (main())
  ├─ Wait for service on port ────► Service ready? ─NO──► ERROR: Exit
  │                                    │ YES
  ├─ Clear database
  ├─ Clear RabbitMQ
  │
  ├─────────────────────────────────────────────────────────────────────────
  │ PHASE 0: WARMUP (NOT MEASURED)
  │─────────────────────────────────────────────────────────────────────────
  ├─ Start monitoring threads:
  │  ├─ ProcessMonitor (500ms interval)
  │  ├─ ContainerMonitor: RabbitMQ (3000ms interval)
  │  ├─ ContainerMonitor: PostgreSQL (3000ms interval)
  │  └─ SystemMonitor (500ms interval)
  │
  ├─ Warmup sequence:
  │  ├─ Start RabbitMQ connection
  │  ├─ Start consumer thread (target: 200 events)
  │  ├─ Sleep 1 second (let consumer set up)
  │  ├─ Publish 200 warmup events (as fast as possible)
  │  ├─ Close publisher connection
  │  ├─ Wait for consumption (timeout: 30s)
  │  ├─ Run k6 API warmup (5s duration)
  │  ├─ Clear database
  │  ├─ Clear RabbitMQ
  │  └─ Reset TestState counters
  │
  ├─────────────────────────────────────────────────────────────────────────
  │ PHASE 1: PUBLISH EVENTS (MEASURED)
  │─────────────────────────────────────────────────────────────────────────
  ├─ Record phase1_start timestamp
  ├─ Start RabbitMQ connection
  ├─ Start consumer thread (target: 10000 events)
  │  └─ Consumer runs until it receives all events or timeout (120s)
  ├─ Sleep 1 second (let consumer declare queue and bind)
  ├─ Publish 10000 events (as fast as possible)
  │  └─ Each event publishes to RabbitMQ immediately
  ├─ Close publisher connection
  ├─ Record phase1_end timestamp
  │
  ├─────────────────────────────────────────────────────────────────────────
  │ PHASE 2: CONSUME EVENTS (MEASURED)
  │─────────────────────────────────────────────────────────────────────────
  ├─ Record phase2_start timestamp
  ├─ Main thread polls consumer progress:
  │  ├─ While TestState.received_event_count < 10000:
  │  │  ├─ Check count with lock
  │  │  └─ Sleep 100ms
  │  └─ Timeout: 120 seconds
  ├─ Record phase2_end timestamp
  │
  ├─────────────────────────────────────────────────────────────────────────
  │ PHASE 3: API LOAD TEST (MEASURED)
  │─────────────────────────────────────────────────────────────────────────
  ├─ Record phase3_start timestamp
  ├─ Run k6 load test:
  │  ├─ Command: k6 run --vus 1 --duration 30s --env API_URL=... api-load-test.js
  │  ├─ k6 executes for 30 seconds
  │  └─ Parse k6 output for metrics
  ├─ Record phase3_end timestamp
  │
  ├─────────────────────────────────────────────────────────────────────────
  │ FINALIZATION
  │─────────────────────────────────────────────────────────────────────────
  ├─ Print summary:
  │  ├─ Phase 1 time: XX.XXX seconds
  │  ├─ Phase 2 time: XX.XXX seconds
  │  ├─ Phase 3 time: XX.XXX seconds
  │  └─ Total time: XX.XXX seconds
  │
  ├─ Stop monitoring threads (with 1s final delay)
  ├─ Generate reports:
  │  ├─ test-report-{timestamp}.json (main report)
  │  ├─ test-report-{timestamp}-resource-metrics.json
  │  ├─ test-report-{timestamp}-events-throughput.json
  │  ├─ test-report-{timestamp}-api-throughput.json
  │  ├─ test-report-{timestamp}-rabbitmq-metrics.json
  │  ├─ test-report-{timestamp}-postgres-metrics.json
  │  ├─ test-report-{timestamp}-system-metrics.json
  │  └─ test-report-{timestamp}-chart.png
  │
  └─ EXIT
```

---

## Concurrent Publisher/Consumer Interaction

### Phase 1 & 2: Concurrent Execution

```
Timeline: Phase 1 Start ──────────────────────────────────────────── Phase 2 End
                │                                                           │
         ┌──────┴──────────────────────────────────────────────────────────┴─────┐
         │                                                                        │
    ╔════╩════════════════════════════════╗  ╔═══════════════════════════════╗
    ║   PHASE 1: MAIN THREAD              ║  ║   PHASE 2: MAIN THREAD        ║
    ║   (Publishes events)                ║  ║   (Waits for consumption)     ║
    ╚════╦════════════════════════════════╝  ╚═╦═════════════════════════════╝
         │                                      │
         │  Start time: T0                      │
         │                                      │
         ├─ Connect to RabbitMQ                 │
         │  (100ms - 1s)                        │
         │                                      │
         ├─ Declare exchange                    │
         │  (50ms)                              │
         │                                      │
         ├─ Start consumer thread               │
         │  └─ Thread target: consume_events()  │
         │                                      │
         ├─ Sleep 1 second                      │
         │  (CONSUMER_SETUP_DELAY_SEC)          │
         │  [Consumer declares queue & binds]   │
         │                                      │
         ├─ Record phase1_start                 │
         │                                      │
         ├─ START PUBLISHING EVENTS ────────────┼──────────────► Consumer receives
         │  (T0 + ~1.2s)                        │                 first events
         │                                      │
         │  For i = 0 to NUM_EVENTS-1:          │
         │    ├─ Publish event to RabbitMQ      │
         │    │  (via basic_publish)            │
         │    └─ No delay (delay=0)             │
         │                                      │
         │  Total publish time:                 │
         │  ~5-10 seconds (for 10000 events)    │
         │                                      │
         │  Publish rate: 1000-2000 events/sec  │
         │                                      │
         ├─ Record phase1_end                   │
         │                                      │
         ├─ PHASE 1 COMPLETE ─────────────────► PHASE 2 START
         │  (close connection)                  │
         │                                      │
         │                                      │ Record phase2_start
         │                                      │
         │                                      │ While received_count < 10000:
         │                                      │   ├─ Read TestState.received_event_count
         │                                      │   ├─ Log progress every 1s
         │                                      │   └─ Sleep 100ms
         │                                      │
         │                                      │ Total wait time: X seconds
         │                                      │
         │                                      ├─ Record phase2_end
         │                                      │
         │                                      └─ Consumer thread joins (timeout: 2s)
```

### Event Flow During Concurrent Phase

```
             RabbitMQ Server
             ┌──────────────────────────────────────────────────┐
             │  Exchange: referenceservice.comparison (topic)    │
             │  ┌─────────────────────────────────────────────┐ │
             │  │  Queue: service-tester                       │ │
             │  │  Binding: instrumentstatus.kpi.updated      │ │
             │  │  Prefetch count: 100                         │ │
             │  │  Durable: yes                                │ │
             │  │  ACK: manual (after processing)             │ │
             │  │                                              │ │
             │  │  [Event 1] [Event 2] ... [Event 10000]      │ │
             │  └─────────────────────────────────────────────┘ │
             └──────────────────────────────────────────────────┘
                     ↑ (publish)                    ↓ (consume)
                     │                              │
        ┌────────────┴──────────────┐    ┌─────────┴─────────────────┐
        │ MAIN THREAD               │    │ CONSUMER THREAD           │
        │ (Publisher)               │    │ (Event Callback)          │
        │                           │    │                           │
        │ for i in range(10000):    │    │ while True:               │
        │   channel.basic_publish() │    │   for each message:       │
        │   (no delay)              │    │     callback() triggered  │
        │                           │    │     │                     │
        │ Rate: 1000-2000 evt/s    │    │     ├─ Increment counter  │
        │                           │    │     ├─ Sample (100ms)     │
        │                           │    │     ├─ Log (1s)           │
        │                           │    │     ├─ ACK message        │
        │                           │    │     └─ Check if done      │
        │                           │    │                           │
        │                           │    │ Rate: variable            │
        │                           │    │  (depends on service)     │
        │                           │    │                           │
        │ Close connection          │    │ Stop consuming            │
        └────────────┬──────────────┘    │ Close connection          │
                     │                    └─────────────────────────┘
                     │
                     └─ Producer ahead of consumer
                        (queue builds up if consumer is slow)
```

---

## TestState Synchronization

### Thread Safety Pattern

```
┌─────────────────────────────────────────┐
│ TestState                               │
│                                         │
│  private long _receivedEventCount = 0   │
│  private object _lock = new object()    │
│                                         │
│  public long ReceivedEventCount {       │
│    get {                                │
│      lock (_lock) {                     │
│        return _receivedEventCount;      │
│      }                                  │
│    }                                    │
│  }                                      │
│                                         │
│  public void IncrementReceivedCount() { │
│    lock (_lock) {                       │
│      _receivedEventCount++;             │
│    }                                    │
│  }                                      │
└─────────────────────────────────────────┘
        ↑                       ↑
        │                       │
   CONSUMER THREAD        MAIN THREAD
   (callback)            (polling)
   
   Calls:                Calls:
   - Increment()         - get ReceivedEventCount
     (100ms+)            - (100ms poll)
     
   Data flow:
   1. Consumer increments counter
   2. Lock protects against race condition
   3. Main thread reads counter atomically
   4. No dirty reads or lost updates
```

### Throughput Sample Collection

```
TestState.ThroughputSamples
│
├─ Type: ConcurrentBag<ThroughputSample>
│
├─ Updated by: Consumer callback (every 100ms)
│  └─ Add() is thread-safe
│
└─ Read by: Report generator (after test)
   └─ Can iterate safely (snapshot)

ThroughputSample {
  Timestamp: DateTime.UtcNow
  ElapsedSeconds: (current_time - start_time)
  TotalEvents: received_count
  EventsPerSecond: (events_in_period / time_in_period)
}

Timeline of samples:
t=0.1s    [Sample 1: 0 events]
t=0.2s    [Sample 2: 50 events, 500 evt/s]
t=0.3s    [Sample 3: 120 events, 700 evt/s]
...
t=XX.Xs   [Sample N: 10000 events, XXX evt/s]
```

---

## Monitoring Architecture

### Four Parallel Monitoring Threads

```
        ┌─────────────────────────────────────────────────────────┐
        │ Main Application                                        │
        │ ┌───────────────────────────────────────────────────┐   │
        │ │ Test Orchestrator (TestOrchestratorService)      │   │
        │ │ ├─ Phase 0: Warmup                              │   │
        │ │ ├─ Phase 1: Publish events                      │   │
        │ │ ├─ Phase 2: Consume events                      │   │
        │ │ └─ Phase 3: API load test                       │   │
        │ └───────────────────────────────────────────────────┘   │
        │              ↑                                            │
        │              │ (requests metrics)                         │
        │              │                                            │
        │  ┌───────────┴──────────┬──────────────┬──────────────┐  │
        │  │                      │              │              │  │
        │  ▼                      ▼              ▼              ▼  │
        │ ┌────────────────┐ ┌─────────┐ ┌─────────┐ ┌──────────┐ │
        │ │ProcessMonitor  │ │Container│ │Container│ │  System  │ │
        │ │                │ │Monitor: │ │Monitor: │ │ Monitor  │ │
        │ │Port monitoring │ │RabbitMQ │ │Postgres │ │          │ │
        │ │                │ │         │ │         │ │          │ │
        │ │ Interval:      │ │Interval:│ │Interval:│ │Interval: │ │
        │ │ 500ms          │ │ 3000ms  │ │ 3000ms  │ │ 500ms    │ │
        │ │                │ │         │ │         │ │          │ │
        │ │Metrics:        │ │Metrics: │ │Metrics: │ │Metrics:  │ │
        │ │- CPU%          │ │- CPU%   │ │- CPU%   │ │- CPU%    │ │
        │ │- Memory MB     │ │- Memory │ │- Memory │ │- Memory  │ │
        │ │- Threads       │ │- I/O    │ │- I/O    │ │- Disk    │ │
        │ │                │ │         │ │         │ │          │ │
        │ │Samples: 200+   │ │Samples: │ │Samples: │ │Samples:  │ │
        │ │per test        │ │~30-50   │ │~30-50   │ │~200+     │ │
        │ └────────────────┘ └─────────┘ └─────────┘ └──────────┘ │
        │                                                           │
        └─────────────────────────────────────────────────────────┘

KEY TIMING:
- All monitors START: Before Phase 0 warmup
- All monitors STOP: After Phase 3 API test
- Final wait: 1 second (FINAL_MONITORING_DELAY_SEC)
  └─ Captures post-test stabilization

SAMPLING INTERVALS:
- Service/System: 500ms (fast, high resolution)
- Containers: 3000ms (slower, less overhead)
- Throughput: 100ms (event-driven in callbacks)
```

---

## k6 Load Testing Sequence

```
PHASE 3: API LOAD TEST
┌──────────────────────────────────────────────────────────────┐
│                                                              │
│ run_k6_test():                                              │
│ ├─ Parse duration: "30s" → 30 seconds                       │
│ ├─ Build command:                                           │
│ │  k6 run --vus 1 --duration 30s --env API_URL=... ...    │
│ │                                                           │
│ ├─ Start k6 process (subprocess)                            │
│ │  └─ Timeout: 30s + 30s = 60s total                       │
│ │                                                           │
│ ├─ k6 execution (in subprocess):                            │
│ │  │                                                        │
│ │  ├─ Load api-load-test.js                               │
│ │  │  │                                                    │
│ │  │  └─ export default function() {                      │
│ │  │       const res = http.get(API_URL)                  │
│ │  │       check(res, {                                   │
│ │  │         'status is 2xx': (r) =>                      │
│ │  │           r.status >= 200 && r.status < 300         │
│ │  │       })                                             │
│ │  │     }                                                │
│ │  │                                                      │
│ │  ├─ Initialize VUs (1 virtual user)                     │
│ │  │                                                      │
│ │  ├─ For 30 seconds:                                     │
│ │  │  ├─ VU calls function()                             │
│ │  │  ├─ Make HTTP GET to API_URL                         │
│ │  │  ├─ Check response status                            │
│ │  │  └─ Print progress line (progress lines)            │
│ │  │     └─ "running (01.0s), 1/1 VUs, 754 complete..." │
│ │  │                                                      │
│ │  └─ Print final summary                                 │
│ │     └─ "http_reqs: 3600   120.000000/s"               │
│ │     └─ "checks_succeeded: 100.00% 3600 out of 3600"   │
│ │                                                        │
│ ├─ Capture full output (stdout + stderr)                 │
│ │                                                        │
│ └─ Parse k6 output:                                      │
│    ├─ Extract total_requests: 3600                       │
│    ├─ Extract success_count: 3600                        │
│    ├─ Extract error_count: 0                             │
│    ├─ Parse progress lines:                              │
│    │  ├─ "running (01.0s)" → 1.0s elapsed               │
│    │  ├─ "754 complete" → 754 iterations at 1.0s        │
│    │  └─ Create sample (1.0, 754)                       │
│    │                                                     │
│    └─ Generate throughput samples:                      │
│       ├─ For each progress point:                       │
│       │  ├─ elapsed_sec = 1.0                           │
│       │  ├─ iterations = 754                            │
│       │  ├─ calls_per_sec = 754 / 1.0 = 754            │
│       │  └─ Append sample to api_throughput_samples    │
│       │                                                 │
│       └─ If no progress data (redirected output):      │
│          ├─ Interpolate samples at 1-second intervals  │
│          ├─ est_requests = (3600 * 1) / 30 = 120      │
│          ├─ est_requests = (3600 * 2) / 30 = 240      │
│          └─ ... up to 30 seconds                       │
│                                                         │
└──────────────────────────────────────────────────────────────┘

Output: (elapsed_time=30.5s, success=3600, errors=0)
```

---

## Metrics Aggregation & Summary Calculation

```
Event Throughput Samples (from consumer callback):
────────────────────────────────────────────────────
[
  { timestamp: "2025-11-15T10:00:01.100", elapsed: 1.1, total: 200, rate: 200 },
  { timestamp: "2025-11-15T10:00:01.200", elapsed: 1.2, total: 350, rate: 150 },
  { timestamp: "2025-11-15T10:00:01.300", elapsed: 1.3, total: 490, rate: 140 },
  ...
  { timestamp: "2025-11-15T10:00:06.500", elapsed: 6.5, total: 10000, rate: 95 }
]

Summary Calculation:
─────────────────────
rates = [200, 150, 140, ..., 95]

avg_events_per_second = sum(rates) / count
                      = 8000 / 100
                      = 80 events/sec

peak_events_per_second = max(rates)
                       = 200 events/sec

min_events_per_second = min(non_zero_rates)
                      = 50 events/sec

std_dev = sqrt(sum((rate - avg)^2) / (count - 1))
        = sqrt(...) / 99
        = 45.2 events/sec

cv_percent = (std_dev / avg) * 100
           = (45.2 / 80) * 100
           = 56.5%

avg_response_time_ms = 1000 / avg_rate
                     = 1000 / 80
                     = 12.5 ms per event

───────────────────────────────────────────
Summary JSON:
{
  "avg_events_per_second": 80.00,
  "peak_events_per_second": 200.00,
  "min_events_per_second": 50.00,
  "std_dev_events_per_second": 45.20,
  "cv_events_per_second": 56.50,  # High variability!
  "avg_response_time_ms": 12.500,
  "total_samples": 100,
  "total_events": 10000
}
```

---

## Error Handling Decision Trees

### Database Cleanup

```
clear_database(port):
│
├─ Get database name from port mapping
│  └─ port 8094 → "go_db"
│
├─ Run clean-service-data.sh (attempt 1)
│  │
│  ├─ Success? ─────► Log "✓ Database cleared"
│  │                  Return
│  │
│  ├─ Timeout (30s)?
│  │  ├─ Log warning
│  │  └─ Retry (attempt 2)
│  │
│  ├─ Error?
│  │  ├─ Log warning
│  │  └─ Retry (attempt 2)
│  │
│  └─ Attempt 2 fails?
│     └─ Raise RuntimeError (abort test)
│
└─ End
```

### Service Port Detection

```
wait_for_service_on_port(port, timeout=30s):
│
├─ Check if service already running:
│  │
│  ├─ Run: lsof -ti :{port}
│  │  ├─ Success + output? ──► Log "✓ Found service (PID: ...)"
│  │  │                        Return True
│  │  │
│  │  ├─ No process found?
│  │  │  └─ Continue below
│  │  │
│  │  └─ lsof not available?
│  │     └─ Fallback to psutil
│  │        └─ Check psutil.net_connections()
│  │
│  └─ (End check)
│
├─ Service not found, start waiting:
│  │
│  ├─ Log warning: "No service detected, waiting..."
│  │
│  ├─ For i = 0 to timeout_seconds:
│  │  │
│  │  ├─ Sleep 1 second
│  │  │
│  │  ├─ Try lsof again
│  │  │  ├─ Success? ──► Log "✓ Found service"
│  │  │  │               Return True
│  │  │  │
│  │  │  └─ Still not found
│  │  │     └─ Log progress every 5 seconds
│  │  │
│  │  └─ (Next iteration)
│  │
│  └─ Timeout expired?
│     ├─ Log error: "Service not found after 30s"
│     └─ Return False (caller will exit)
│
└─ End
```

### Warmup Phase Error Handling

```
execute_warmup_phase():
│
├─ Try:
│  │
│  ├─ Publish WARMUP_EVENTS (200)
│  ├─ Consume WARMUP_EVENTS (200)
│  ├─ Run k6 warmup (5s)
│  ├─ Clear database
│  ├─ Clear RabbitMQ
│  └─ Reset counters
│
├─ Exception caught?
│  │
│  ├─ Log warning: "Warmup failed: {exception}"
│  │
│  ├─ Log note: "Continuing with cold-start metrics"
│  │
│  └─ Reset counters anyway:
│     ├─ received_event_count = 0
│     ├─ throughput_samples.clear()
│     └─ api_throughput_samples.clear()
│
└─ Continue to Phase 1
   (Warmup failure doesn't abort test)
```

---

## Report File Generation

### Filename Pattern

```
test-report-{YYYYMMDD_HHMMSS}-{program_name}.{suffix}

Example:
test-report-20251022_122306-goReferenceService.json
test-report-20251022_122306-goReferenceService.resource-metrics.json
test-report-20251022_122306-goReferenceService.events-throughput.json
test-report-20251022_122306-goReferenceService.api-throughput.json
test-report-20251022_122306-goReferenceService.rabbitmq-metrics.json
test-report-20251022_122306-goReferenceService.postgres-metrics.json
test-report-20251022_122306-goReferenceService.system-metrics.json
test-report-20251022_122306-goReferenceService.chart.png

Format: YYYYMMDD_HHMMSS
├─ Year: 2025
├─ Month: 11 (November)
├─ Day: 22
├─ Hour: 12 (24-hour format)
├─ Minute: 23
└─ Second: 06

All reports use same timestamp (generated after all metrics collected)
```

### File Generation Sequence

```
After Phase 3 (API test) completes:
│
├─ Wait FINAL_MONITORING_DELAY_SEC (1.0s)
│  └─ Capture final metrics samples
│
├─ Stop all monitors and collect their data:
│  │
│  ├─ ProcessMonitor.save_report()
│  │  └─ → test-report-{ts}-{program}.resource-metrics.json
│  │
│  ├─ ContainerMonitor(rabbitmq).save_report()
│  │  └─ → test-report-{ts}-{program}.rabbitmq-metrics.json
│  │
│  ├─ ContainerMonitor(postgres).save_report()
│  │  └─ → test-report-{ts}-{program}.postgres-metrics.json
│  │
│  └─ SystemMonitor.save_report()
│     └─ → test-report-{ts}-{program}.system-metrics.json
│
├─ Generate throughput reports from samples:
│  │
│  ├─ save_throughput_report(throughput_samples, "events")
│  │  └─ → test-report-{ts}-{program}.events-throughput.json
│  │     ├─ samples: [...] (all 100ms sampled data)
│  │     └─ summary: { avg, peak, min, std_dev, cv, ... }
│  │
│  └─ save_throughput_report(api_throughput_samples, "calls")
│     └─ → test-report-{ts}-{program}.api-throughput.json
│        ├─ samples: [...] (from k6 output)
│        └─ summary: { avg, peak, min, std_dev, cv, ... }
│
├─ Write main test report:
│  │
│  ├─ Get system info
│  ├─ Compile all timing data
│  ├─ Get process info (name, PID, port)
│  ├─ Include phase timestamps
│  ├─ Calculate throughput summaries
│  │
│  └─ → test-report-{ts}-{program}.json
│     ├─ test_date: "2025-11-22 12:23:06"
│     ├─ total_runtime_seconds: 36.543
│     ├─ system: { os, processor_count, ... }
│     ├─ phase_timestamps: { phase1_start, phase1_end, ... }
│     ├─ monitored_process: { name, pid, port }
│     ├─ configuration: { num_events, api_duration, ... }
│     └─ results:
│        ├─ phase1_publish: { duration, throughput }
│        ├─ phase2_consume: { duration, throughput }
│        └─ phase3_api: { duration, requests, success%, errors%, ... }
│
├─ Generate metrics chart:
│  │
│  └─ generate_metrics_chart(...)
│     └─ → test-report-{ts}-{program}.chart.png
│        ├─ Subplot 1: Event throughput over time
│        ├─ Subplot 2: API throughput over time
│        └─ Subplot 3: Resource usage (CPU, Memory)
│
└─ All reports written to RESULTS_FOLDER (default: ./test-results)
```

---

## Configuration Environment Variables

### RabbitMQ Settings

```
RABBITMQ_HOST           (default: localhost)
RABBITMQ_PORT           (default: 5672)
RABBITMQ_USER           (default: admin)
RABBITMQ_PASS           (default: admin)
RABBITMQ_EXCHANGE       (default: referenceservice.comparison)
RABBITMQ_EVENT_TYPE     (default: instrument.status.changed)
RABBITMQ_CONSUME_EVENT_TYPE (default: instrumentstatus.kpi.updated)
RABBITMQ_CONSUMER_QUEUE (default: service-tester)
RABBITMQ_PREFETCH_COUNT (default: 100)
  └─ Bun exception: 1 (framework limitation)
  └─ All others: 100 (standardized for fair comparison)
RABBITMQ_CONTAINER_NAME (default: performancetest-rabbitmq)
```

### Testing Settings

```
TEST_EVENTS             (default: 10000)
TEST_API_DURATION       (default: 30s)
TEST_API_WORKERS        (default: 1)
TEST_API_PORT           (default: 8080)
TEST_RESULTS_FOLDER     (default: ./test-results)
TEST_WARMUP_EVENTS      (default: 200)
TEST_WARMUP_DURATION    (default: 5s)
```

### Monitoring Settings

```
MONITOR_SERVICE_INTERVAL_MS    (default: 500)
MONITOR_CONTAINER_INTERVAL_MS  (default: 3000)
MONITOR_FINAL_WAIT_SEC         (default: 1.0)
POSTGRES_CONTAINER_NAME        (default: performancetest-postgres)
```

### Docker Container Details

```
RabbitMQ:
  Container: performancetest-rabbitmq
  Port: 5672 (AMQP)
  Port: 15672 (Management UI)
  Credentials: admin/admin
  Environment:
    RABBITMQ_DEFAULT_USER: admin
    RABBITMQ_DEFAULT_PASS: admin

PostgreSQL:
  Container: performancetest-postgres
  Port: 5432
  Credentials: admin/admin
  Databases: {service}_db for each implementation
    - bun_db
    - dotnet9_db
    - dotnet9aot_db
    - go_db
    - java21quarkusgraal_db
    - java21springboot_db
    - java21springbootgraal_db
    - python_db
    - rust_db
    - java25springboot_db
    - java25springbootgraal_db
    - java25quarkusgraal_db
```

---

## Data Flow Summary

```
User runs: python service-tester.py --port 8094 --events 10000 --duration 30s
│
├─ Parsed arguments
│  └─ API_PORT=8094, NUM_EVENTS=10000, API_DURATION="30s"
│
├─ wait_for_service_on_port(8094)
│  └─ Detects running Go service (port 8094)
│
├─ Monitors start (4 background threads)
│  └─ Begin sampling every 500ms or 3000ms
│
├─ PHASE 0: WARMUP (200 events)
│  ├─ publish_events() → RabbitMQ
│  ├─ consume_events() → Callback increments counter
│  └─ API warmup with k6
│
├─ PHASE 1: PUBLISH (10000 events)
│  ├─ Start consumer thread
│  └─ Publish 10000 events as fast as possible
│     └─ Rate: 1000-2000 events/sec to RabbitMQ queue
│
├─ PHASE 2: CONSUME (10000 events)
│  ├─ Main thread polls: received_count >= 10000?
│  ├─ Callback (consumer thread):
│  │  ├─ Increments counter (with lock)
│  │  ├─ Samples throughput every 100ms
│  │  ├─ Logs progress every 1s
│  │  └─ ACKs message after processing
│  └─ Main thread waits (timeout: 120s)
│
├─ PHASE 3: API LOAD (30s with k6)
│  ├─ Run k6 subprocess
│  ├─ k6 makes HTTP GET requests to service
│  ├─ Service processes requests from database
│  ├─ k6 outputs progress (1s intervals)
│  └─ Parse output for metrics
│
├─ Monitoring stops (all background threads)
│  └─ Collected ~200 samples per metric
│
└─ Report generation
   ├─ Aggregate all metrics
   ├─ Calculate summaries (avg, peak, std dev, CV)
   ├─ Generate JSON reports (8 files)
   └─ Generate visualization chart (PNG)
```

This comprehensive workflow ensures:
1. Accurate measurement of steady-state performance
2. Thread-safe concurrent operations
3. Detailed metric collection across all layers
4. Reproducible results with consistent sampling
5. Complete traceability of all phases with timestamps
