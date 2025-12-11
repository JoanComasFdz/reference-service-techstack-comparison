# Quick Reference: Python to .NET Orchestration Patterns

Rapid lookup for key patterns and their .NET equivalents.

---

## Core Patterns

### 1. Thread-Safe Counter with Lock

**Python:**
```python
class TestState:
    received_event_count: int = 0
    received_lock: threading.Lock = field(default_factory=threading.Lock)

with test_state.received_lock:
    test_state.received_event_count += 1
    count = test_state.received_event_count
```

**C#/.NET:**
```csharp
public class TestState
{
    private long _receivedEventCount = 0;
    private readonly object _lock = new object();
    
    public void IncrementReceivedEventCount()
    {
        lock (_lock)
        {
            _receivedEventCount++;
        }
    }
    
    public long ReceivedEventCount
    {
        get
        {
            lock (_lock)
            {
                return _receivedEventCount;
            }
        }
    }
}
```

---

### 2. Thread-Safe List

**Python:**
```python
test_state.throughput_samples: list[dict] = field(default_factory=list)

# In callback (any thread):
test_state.throughput_samples.append(sample_dict)

# Main thread reads (after test):
for sample in test_state.throughput_samples:
    print(sample)
```

**C#/.NET:**
```csharp
public ConcurrentBag<ThroughputSample> ThroughputSamples { get; } 
    = new ConcurrentBag<ThroughputSample>();

// In callback (any thread):
testState.ThroughputSamples.Add(sample);

// Main thread reads (after test):
foreach (var sample in testState.ThroughputSamples.OrderBy(s => s.Timestamp))
{
    Console.WriteLine(sample);
}
```

---

### 3. Background Thread (Daemon)

**Python:**
```python
def consume_events(test_state: TestState) -> None:
    # Consumer logic
    pass

# Start daemon thread
consumer_thread = threading.Thread(target=consume_events, args=(test_state,), daemon=True)
consumer_thread.start()

# Main thread continues (daemon thread won't block shutdown)
```

**C#/.NET (using Task.Run):**
```csharp
async Task ConsumeEventsAsync(TestState testState, CancellationToken ct)
{
    // Consumer logic
    await Task.Delay(100, ct);
}

// Start background task (don't await immediately)
var consumerTask = Task.Run(
    () => ConsumeEventsAsync(testState, cancellationToken), 
    cancellationToken);

// Main thread continues (don't block on task)
await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
```

---

### 4. Concurrent Publish/Consume

**Python:**
```python
# Start consumer in thread
consumer_thread = threading.Thread(target=consume_events, args=(test_state,), daemon=True)
consumer_thread.start()

# Give consumer time to set up
time.sleep(1)

# Main thread publishes while consumer runs
publish_start = time.time()
send_multiple_events(channel, NUM_EVENTS, delay=0)
publish_time = time.time() - publish_start

# Main thread waits for consumption
while time.time() - consume_start < 120:
    if test_state.received_event_count >= NUM_EVENTS:
        break
    time.sleep(0.1)

# Join consumer thread
consumer_thread.join(timeout=2)
```

**C#/.NET:**
```csharp
// Start consumer task (don't await)
var consumerTask = Task.Run(
    async () => await ConsumeEventsAsync(testState, cancellationToken),
    cancellationToken);

// Give consumer time to set up
await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

// Main thread publishes while consumer runs
var publishStart = DateTime.UtcNow;
await PublishEventsAsync(config.NumEvents, TimeSpan.Zero, cancellationToken);
var publishTime = DateTime.UtcNow - publishStart;

// Main thread waits for consumption
var consumeDeadline = DateTime.UtcNow.AddSeconds(120);
while (DateTime.UtcNow < consumeDeadline)
{
    if (testState.ReceivedEventCount >= config.NumEvents)
        break;
    await Task.Delay(100, cancellationToken);
}

// Wait for consumer task (timeout: 2s)
try
{
    await consumerTask.ConfigureAwait(false).WaitAsync(TimeSpan.FromSeconds(2));
}
catch (TimeoutException) { }
```

---

### 5. Sampling with Time Checks

**Python:**
```python
last_sample_time = start_time
last_sample_count = 0

def callback(ch, method, properties, body):
    nonlocal last_sample_time, last_sample_count
    
    with test_state.received_lock:
        test_state.received_event_count += 1
        count = test_state.received_event_count
    
    current_time = time.time()
    
    # Sample every 100ms
    if current_time - last_sample_time >= 0.1:
        events_in_period = count - last_sample_count
        elapsed_since_sample = current_time - last_sample_time
        events_per_sec = events_in_period / elapsed_since_sample
        
        test_state.throughput_samples.append({
            "timestamp": datetime.now().isoformat(),
            "elapsed_seconds": current_time - start_time,
            "total_events": count,
            "events_per_second": events_per_sec
        })
        
        last_sample_time = current_time
        last_sample_count = count
```

**C#/.NET:**
```csharp
private DateTime _lastSampleTime;
private long _lastSampleCount;
private const long SamplingIntervalMs = 100;

private void HandleMessage(long count, DateTime currentTime)
{
    double msSinceSample = (currentTime - _lastSampleTime).TotalMilliseconds;
    
    // Sample every 100ms
    if (msSinceSample >= SamplingIntervalMs)
    {
        long eventsInPeriod = count - _lastSampleCount;
        double elapsedSinceSample = (currentTime - _lastSampleTime).TotalSeconds;
        double eventsPerSec = eventsInPeriod / elapsedSinceSample;
        
        testState.ThroughputSamples.Add(new ThroughputSample
        {
            Timestamp = currentTime,
            ElapsedSeconds = _stopwatch.Elapsed.TotalSeconds,
            TotalCount = count,
            RatePerSecond = eventsPerSec
        });
        
        _lastSampleTime = currentTime;
        _lastSampleCount = count;
    }
}
```

---

### 6. Subprocess Execution (k6)

**Python:**
```python
def run_k6_test(script_path: str, api_url: str, duration: str, vus: int):
    cmd = ['k6', 'run', '--vus', str(vus), '--duration', duration,
           '--env', f'API_URL={api_url}', script_path]
    
    start_time = time.time()
    timeout_seconds = parse_duration_to_seconds(duration) + 30
    
    try:
        result = subprocess.run(
            f'{" ".join(cmd)} 2>&1 | tee output.txt',
            shell=True,
            timeout=timeout_seconds
        )
    except subprocess.TimeoutExpired:
        print("k6 timed out")
        return (0, 0, 0)
    
    with open('output.txt', 'r') as f:
        output = f.read()
    
    # Parse output
    total_requests, success, errors = parse_k6_output(output)
    elapsed = time.time() - start_time
    
    return (elapsed, success, errors)
```

**C#/.NET:**
```csharp
public async Task<(TimeSpan duration, int success, int errors)> 
    RunK6TestAsync(string scriptPath, string apiUrl, string duration, int vus)
{
    var timeoutSeconds = ParseDurationToSeconds(duration) + 30;
    var startTime = DateTime.UtcNow;
    
    var psi = new ProcessStartInfo
    {
        FileName = "k6",
        Arguments = $"run --vus {vus} --duration {duration} " +
                   $"--env API_URL={apiUrl} {scriptPath}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    
    using var process = Process.Start(psi) 
        ?? throw new InvalidOperationException("Failed to start k6");
    
    var output = new StringBuilder();
    process.OutputDataReceived += (s, e) =>
    {
        if (e.Data != null)
            output.AppendLine(e.Data);
    };
    process.BeginOutputReadLine();
    
    bool completed = process.WaitForExit((int)(timeoutSeconds * 1000));
    
    if (!completed)
    {
        process.Kill();
        return (TimeSpan.Zero, 0, 0);
    }
    
    var elapsed = DateTime.UtcNow - startTime;
    var (total, success, errors) = ParseK6Output(output.ToString());
    
    return (elapsed, success, errors);
}
```

---

### 7. Regex Parsing

**Python:**
```python
import re

# Single value
match = re.search(r'http_reqs[.\s]*:\s*(\d+)', output)
if match:
    total_requests = int(match.group(1))

# Multiple values
match = re.search(
    r'checks_succeeded[.\s]*:\s*([\d.]+)%\s*(\d+)\s*out of\s*(\d+)',
    output)
if match:
    success_count = int(match.group(2))
    total_checks = int(match.group(3))

# Multiline search
progress_pattern = r'running \((\d+\.\d+)s\).*?(\d+) complete'
for line in output.split('\n'):
    match = re.search(progress_pattern, line)
    if match:
        elapsed_sec = float(match.group(1))
        complete_iterations = int(match.group(2))
```

**C#/.NET:**
```csharp
using System.Text.RegularExpressions;

// Single value
var match = Regex.Match(output, @"http_reqs[.\s]*:\s*(\d+)");
if (match.Success)
{
    int totalRequests = int.Parse(match.Groups[1].Value);
}

// Multiple values
match = Regex.Match(output, 
    @"checks_succeeded[.\s]*:\s*([\d.]+)%\s*(\d+)\s*out of\s*(\d+)");
if (match.Success)
{
    int successCount = int.Parse(match.Groups[2].Value);
    int totalChecks = int.Parse(match.Groups[3].Value);
}

// Multiline search
var progressPattern = new Regex(
    @"running \((\d+\.\d+)s\).*?(\d+) complete");
foreach (var line in output.Split('\n'))
{
    match = progressPattern.Match(line);
    if (match.Success)
    {
        double elapsedSec = double.Parse(match.Groups[1].Value);
        int completeIterations = int.Parse(match.Groups[2].Value);
    }
}
```

---

### 8. JSON Serialization

**Python:**
```python
import json

data = {
    "test_date": datetime.now().strftime('%Y-%m-%d %H:%M:%S'),
    "total_runtime_seconds": total_time,
    "results": {
        "phase1": {
            "duration_seconds": publish_time,
            "throughput": num_events / publish_time
        }
    }
}

with open(filename, 'w') as f:
    json.dump(data, f, indent=2)
```

**C#/.NET:**
```csharp
using System.Text.Json;

var data = new
{
    test_date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
    total_runtime_seconds = totalTime,
    results = new
    {
        phase1 = new
        {
            duration_seconds = publishTime.TotalSeconds,
            throughput = numEvents / publishTime.TotalSeconds
        }
    }
};

var json = JsonSerializer.Serialize(data, 
    new JsonSerializerOptions { WriteIndented = true });

await File.WriteAllTextAsync(filename, json);
```

---

### 9. Statistics Calculation

**Python:**
```python
import statistics

def calculate_cv(values: list[float]) -> float:
    """Coefficient of Variation (%)"""
    if not values or len(values) < 2:
        return 0.0
    mean = statistics.mean(values)
    std_dev = statistics.stdev(values)
    return (std_dev / mean) * 100 if mean != 0 else 0.0

rates = [100, 150, 120, 110, 140]
cv = calculate_cv(rates)  # 15.4% (variability)
```

**C#/.NET:**
```csharp
private double CalculateCv(List<double> values)
{
    if (values.Count < 2)
        return 0.0;
    
    double mean = values.Average();
    if (mean == 0)
        return 0.0;
    
    double sumOfSquares = values.Sum(v => Math.Pow(v - mean, 2));
    double stdDev = Math.Sqrt(sumOfSquares / (values.Count - 1));
    
    return (stdDev / mean) * 100;
}

var rates = new List<double> { 100, 150, 120, 110, 140 };
double cv = CalculateCv(rates);  // 15.4 (variability)
```

---

### 10. Logging

**Python:**
```python
def log(message):
    print(message)

log("Starting test...")
log(f"Phase 1 completed in {elapsed:.3f}s")
log("=" * 80)
```

**C#/.NET:**
```csharp
private readonly ILogger<TestOrchestratorService> _logger;

_logger.LogInformation("Starting test...");
_logger.LogInformation(
    "Phase 1 completed in {Elapsed:F3}s", 
    elapsed.TotalSeconds);
_logger.LogWarning("Database cleanup failed: {Error}", ex.Message);
_logger.LogDebug("Detail: {Detail}", debugInfo);
```

---

## Timing Patterns

### Parse Duration String

**Python:**
```python
def parse_duration_to_seconds(duration_str: str) -> int:
    duration_str = duration_str.lower()
    if duration_str.endswith('s'):
        return int(duration_str[:-1])
    elif duration_str.endswith('m'):
        return int(duration_str[:-1]) * 60
    elif duration_str.endswith('h'):
        return int(duration_str[:-1]) * 3600
    else:
        return 30  # Default

seconds = parse_duration_to_seconds("30s")  # 30
seconds = parse_duration_to_seconds("5m")   # 300
seconds = parse_duration_to_seconds("2h")   # 7200
```

**C#/.NET:**
```csharp
private TimeSpan ParseDurationToSeconds(string duration)
{
    if (duration.EndsWith("s"))
        return TimeSpan.FromSeconds(int.Parse(duration[..^1]));
    if (duration.EndsWith("m"))
        return TimeSpan.FromMinutes(int.Parse(duration[..^1]));
    if (duration.EndsWith("h"))
        return TimeSpan.FromHours(int.Parse(duration[..^1]));
    return TimeSpan.FromSeconds(30);  // Default
}

var ts = ParseDurationToSeconds("30s");  // 30 seconds
var ts = ParseDurationToSeconds("5m");   // 5 minutes
```

---

### Elapsed Time Measurement

**Python:**
```python
start_time = time.time()

# ... do work ...

elapsed = time.time() - start_time
print(f"Elapsed: {elapsed:.3f}s")  # Prints: Elapsed: 5.123s
```

**C#/.NET:**
```csharp
var startTime = DateTime.UtcNow;

// ... do work ...

var elapsed = DateTime.UtcNow - startTime;
Console.WriteLine($"Elapsed: {elapsed.TotalSeconds:F3}s");  // Prints: Elapsed: 5.123s
```

---

### Polling with Timeout

**Python:**
```python
start_time = time.time()
timeout_seconds = 30

while True:
    if condition_met():
        break
    
    elapsed = time.time() - start_time
    if elapsed >= timeout_seconds:
        log(f"Timeout after {timeout_seconds}s")
        break
    
    time.sleep(0.1)  # Poll interval
```

**C#/.NET:**
```csharp
var startTime = DateTime.UtcNow;
var timeout = TimeSpan.FromSeconds(30);

while (true)
{
    if (ConditionMet())
        break;
    
    var elapsed = DateTime.UtcNow - startTime;
    if (elapsed >= timeout)
    {
        _logger.LogWarning("Timeout after {Seconds}s", timeout.TotalSeconds);
        break;
    }
    
    await Task.Delay(100, cancellationToken);  // Poll interval
}
```

---

### Recording Phase Timestamps

**Python:**
```python
total_start_time = time.time()

phase_timestamps = {
    "phase1_start": None,
    "phase1_end": None,
    "phase2_start": None,
    "phase2_end": None,
    "phase3_start": None,
    "phase3_end": None
}

phase_timestamps["phase1_start"] = time.time() - total_start_time  # Relative time
# ... do phase 1 ...
phase_timestamps["phase1_end"] = time.time() - total_start_time
```

**C#/.NET:**
```csharp
var totalStartTime = DateTime.UtcNow;

var phaseTimestamps = new Dictionary<string, TimeSpan?>
{
    ["phase1_start"] = null,
    ["phase1_end"] = null,
    ["phase2_start"] = null,
    ["phase2_end"] = null,
    ["phase3_start"] = null,
    ["phase3_end"] = null
};

phaseTimestamps["phase1_start"] = DateTime.UtcNow - totalStartTime;  // Relative time
// ... do phase 1 ...
phaseTimestamps["phase1_end"] = DateTime.UtcNow - totalStartTime;
```

---

## Configuration Patterns

### Environment Variables with Defaults

**Python:**
```python
RABBITMQ_HOST = os.getenv('RABBITMQ_HOST', 'localhost')
RABBITMQ_PORT = int(os.getenv('RABBITMQ_PORT', '5672'))
NUM_EVENTS = int(os.getenv('NUM_EVENTS', '10000'))
API_DURATION = os.getenv('API_DURATION', '30s')
```

**C#/.NET:**
```csharp
public class TestConfiguration
{
    public string RabbitMqHost { get; set; } 
        = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
    
    public int RabbitMqPort { get; set; } 
        = int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_PORT") ?? "5672");
    
    public int NumEvents { get; set; } 
        = int.Parse(Environment.GetEnvironmentVariable("NUM_EVENTS") ?? "10000");
    
    public string ApiDuration { get; set; } 
        = Environment.GetEnvironmentVariable("API_DURATION") ?? "30s";
}
```

---

### Command-Line Arguments

**Python:**
```python
parser = argparse.ArgumentParser()
parser.add_argument('--events', type=int, default=10000)
parser.add_argument('--api-duration', type=str, default='30s')
parser.add_argument('--port', type=int, default=8080)
args = parser.parse_args()

NUM_EVENTS = args.events
API_DURATION = args.api_duration
API_PORT = args.port
```

**C#/.NET:**
```csharp
var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var config = new TestConfiguration
        {
            NumEvents = int.Parse(context.Configuration["NumEvents"] ?? "10000"),
            ApiDuration = context.Configuration["ApiDuration"] ?? "30s",
            ApiPort = int.Parse(context.Configuration["ApiPort"] ?? "8080")
        };
        services.AddSingleton(config);
    });

// Or with explicit CLI parsing:
var index = args.IndexOf("--events");
int numEvents = index >= 0 ? int.Parse(args[index + 1]) : 10000;
```

---

## Error Handling

### Try-Except with Cleanup

**Python:**
```python
try:
    # Attempt operation
    result = risky_operation()
    
except TimeoutError as e:
    log(f"Operation timed out: {e}")
    # Handle timeout
    
except Exception as e:
    log(f"Error: {type(e).__name__}: {e}")
    # Handle general error
    
finally:
    # Always cleanup
    cleanup()
```

**C#/.NET:**
```csharp
try
{
    // Attempt operation
    var result = await RiskyOperationAsync();
}
catch (TimeoutException ex)
{
    _logger.LogError(ex, "Operation timed out");
    // Handle timeout
}
catch (Exception ex)
{
    _logger.LogError(ex, "Error: {ErrorType}", ex.GetType().Name);
    // Handle general error
}
finally
{
    // Always cleanup
    Cleanup();
}
```

---

## BackgroundService Pattern (IHost)

**C#/.NET:**
```csharp
public class MyBackgroundService : BackgroundService
{
    private readonly ILogger<MyBackgroundService> _logger;
    private readonly IHostApplicationLifetime _appLifetime;
    
    public MyBackgroundService(
        ILogger<MyBackgroundService> logger,
        IHostApplicationLifetime appLifetime)
    {
        _logger = logger;
        _appLifetime = appLifetime;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Service starting...");
            
            // Long-running operation
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Doing work...");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            
            _logger.LogInformation("Service completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service failed");
        }
        finally
        {
            // Signal host to stop
            _appLifetime.StopApplication();
        }
    }
}

// Program.cs
var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<MyBackgroundService>();
    });

await builder.Build().RunAsync();
```

---

## Key Differences at a Glance

| Concept | Python | C#/.NET |
|---------|--------|---------|
| Thread lock | `threading.Lock()` | `lock(_object)` |
| Thread-safe list | Manual with lock | `ConcurrentBag<T>` |
| Background thread | `threading.Thread(daemon=True)` | `Task.Run()` |
| Sleep | `time.sleep(1)` | `await Task.Delay(1000)` |
| Exception | `except Exception as e` | `catch (Exception ex)` |
| Finally | `finally:` | `finally { }` |
| Datetime | `time.time()` | `DateTime.UtcNow` |
| Duration | `seconds: float` | `TimeSpan` |
| JSON | `json.dump()` | `JsonSerializer.Serialize()` |
| Files | `open()`, `write()` | `File.WriteAllTextAsync()` |
| Process | `subprocess.run()` | `Process.Start()` |
| Logging | `print()` | `ILogger<T>` |
| Config | Environment vars | `IConfiguration` |
| Async | `async`/`await` | `async`/`await` |
| Collections | `list[]`, `dict{}` | `List<T>`, `Dictionary<K,V>` |

---

## Testing Checklist for .NET Implementation

- [ ] TestState.ReceivedEventCount is thread-safe (lock protected)
- [ ] ThroughputSamples uses ConcurrentBag<T>
- [ ] Publisher and consumer run concurrently (Task.Run, no await)
- [ ] Consumer callback increments counter atomically
- [ ] Throughput sampling happens every 100ms
- [ ] Phase timestamps are relative to start (TimeSpan)
- [ ] All durations are TimeSpan (not milliseconds/ticks)
- [ ] k6 process is awaited with timeout
- [ ] k6 output parsing handles both progress and interpolated samples
- [ ] JSON reports match Python format (indent: 2 spaces)
- [ ] All timestamps are ISO format (UTC)
- [ ] Warmup cleanup resets counters even on failure
- [ ] Database/RabbitMQ cleanup retries (2 attempts)
- [ ] Service port detection uses platform-specific tools
- [ ] Logging uses ILogger<T> (not Console.WriteLine)
- [ ] All async methods propagate CancellationToken
- [ ] BackgroundService stops via _appLifetime.StopApplication()
- [ ] Configuration uses IConfiguration or class injection
- [ ] Environment variables have sensible defaults
- [ ] Final monitoring delay captures last samples (1s wait)
