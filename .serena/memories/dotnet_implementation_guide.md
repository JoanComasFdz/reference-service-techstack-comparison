# .NET Implementation Guide for Service Tester Orchestration

Detailed guidance for replicating the Python service-tester in .NET using BackgroundService and IHost patterns.

---

## Core Architecture

### Project Structure
```
ServiceTester/
├── ServiceTester.csproj
├── Program.cs                           # IHost configuration
├── Models/
│   ├── TestState.cs                     # Thread-safe state
│   ├── TestConfiguration.cs
│   ├── TestMetrics.cs
│   ├── ThroughputSample.cs
│   └── TestReport.cs
├── Services/
│   ├── ITestOrchestrator.cs
│   ├── TestOrchestratorService.cs       # Main orchestration logic
│   ├── IProcessMonitor.cs
│   ├── ProcessMonitorService.cs
│   ├── IContainerMonitor.cs
│   ├── ContainerMonitorService.cs
│   ├── ISystemMonitor.cs
│   ├── SystemMonitorService.cs
│   ├── IApiTester.cs
│   ├── ApiTesterService.cs              # k6 + API load testing
│   ├── IEventPublisher.cs
│   ├── RabbitMqEventPublisher.cs
│   ├── IEventConsumer.cs
│   ├── RabbitMqEventConsumer.cs
│   ├── IReportGenerator.cs
│   └── ReportGeneratorService.cs
├── Utilities/
│   ├── FileUtilities.cs
│   ├── ProcessUtilities.cs
│   ├── MonitoringUtilities.cs
│   └── ConfigurationUtilities.cs
└── Results/
    └── (generated test-report-*.json files)
```

---

## 1. TestState Model

### Implementation
```csharp
public class TestState
{
    // Event consumption counter
    private long _receivedEventCount = 0;
    private readonly object _lock = new object();
    
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
    
    public void IncrementReceivedEventCount()
    {
        lock (_lock)
        {
            _receivedEventCount++;
        }
    }
    
    public void ResetReceivedEventCount()
    {
        lock (_lock)
        {
            _receivedEventCount = 0;
        }
    }
    
    // Throughput samples (thread-safe collection)
    public ConcurrentBag<ThroughputSample> ThroughputSamples { get; } 
        = new ConcurrentBag<ThroughputSample>();
    
    public ConcurrentBag<ThroughputSample> ApiThroughputSamples { get; } 
        = new ConcurrentBag<ThroughputSample>();
    
    public void ClearSamples()
    {
        while (ThroughputSamples.TryTake(out _)) { }
        while (ApiThroughputSamples.TryTake(out _)) { }
    }
}

public class ThroughputSample
{
    public DateTime Timestamp { get; set; }
    public double ElapsedSeconds { get; set; }
    public long TotalCount { get; set; }
    public double RatePerSecond { get; set; }
}
```

### Key Differences from Python
- Use `lock` statement instead of `threading.Lock`
- Use `ConcurrentBag<T>` for thread-safe list (vs Python list + manual sync)
- Expose properties with internal synchronization (vs accessing locked variable directly)

---

## 2. RabbitMQ Event Publisher

### Event Model (CloudEvents v1.0)
```csharp
public class CloudEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; }
    
    [JsonPropertyName("specversion")]
    public string SpecVersion { get; set; } = "1.0";
    
    [JsonPropertyName("source")]
    public string Source { get; set; }
    
    [JsonPropertyName("type")]
    public string Type { get; set; }
    
    [JsonPropertyName("time")]
    public DateTime Time { get; set; }
    
    [JsonPropertyName("datacontenttype")]
    public string DataContentType { get; set; } = "application/json";
    
    [JsonPropertyName("data")]
    public Dictionary<string, object> Data { get; set; }
}

public class EventData
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; }
    
    [JsonPropertyName("previousStatus")]
    public string PreviousStatus { get; set; }
    
    [JsonPropertyName("currentStatus")]
    public string CurrentStatus { get; set; }
}
```

### Publisher Implementation
```csharp
public class RabbitMqEventPublisher : IEventPublisher
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    
    private const int NumDevices = 5;
    private const int NumStatusPairs = 3;
    private static readonly string[] DeviceIds = 
        Enumerable.Range(1, NumDevices).Select(i => $"DEVICE-{i:D3}").ToArray();
    private static readonly string[] Statuses = 
        { "IDLE", "RUNNING", "ERROR", "MAINTENANCE", "OFFLINE" };
    private static readonly (string, string)[] StatusPairs = 
        { ("IDLE", "RUNNING"), ("RUNNING", "ERROR"), ("ERROR", "IDLE") };
    
    public async Task PublishEventsAsync(int count, TimeSpan delay, 
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var channel = connection.CreateModel();
        
        var exchangeName = _configuration["RabbitMq:Exchange"];
        var eventType = _configuration["RabbitMq:EventType"];
        
        channel.ExchangeDeclare(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: true);
        
        var deviceStates = DeviceIds.ToDictionary(
            d => d,
            d => (current: Statuses[Random.Shared.Next(Statuses.Length)], 
                  pairIndex: Random.Shared.Next(NumStatusPairs)));
        
        var startTime = DateTime.UtcNow;
        
        for (int i = 0; i < count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
            
            var deviceId = DeviceIds[Random.Shared.Next(DeviceIds.Length)];
            var state = deviceStates[deviceId];
            
            var statusPair = StatusPairs[state.pairIndex];
            var previousStatus = state.current;
            var currentStatus = previousStatus == statusPair.Item1 
                ? statusPair.Item2 
                : statusPair.Item1;
            
            var cloudEvent = new CloudEvent
            {
                Id = Guid.NewGuid().ToString(),
                Source = "urn:dotnet:event-generator",
                Type = eventType,
                Time = DateTime.UtcNow,
                Data = new Dictionary<string, object>
                {
                    ["deviceId"] = deviceId,
                    ["previousStatus"] = previousStatus,
                    ["currentStatus"] = currentStatus
                }
            };
            
            var body = JsonSerializer.SerializeToUtf8Bytes(cloudEvent);
            
            var props = channel.CreateBasicProperties();
            props.ContentType = "application/json";
            props.DeliveryMode = 2; // Persistent
            
            channel.BasicPublish(
                exchange: exchangeName,
                routingKey: eventType,
                basicProperties: props,
                body: body);
            
            deviceStates[deviceId] = (currentStatus, state.pairIndex);
            
            if (delay > TimeSpan.Zero && i < count - 1)
                await Task.Delay(delay, cancellationToken);
        }
        
        var elapsed = DateTime.UtcNow - startTime;
        _logger.LogInformation(
            "Published {Count} events in {Elapsed}s ({Rate} events/sec)",
            count, elapsed.TotalSeconds, count / elapsed.TotalSeconds);
    }
}
```

---

## 3. RabbitMQ Event Consumer

### Concurrent Consumption Model

```csharp
public class RabbitMqEventConsumer : IEventConsumer
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMqEventConsumer> _logger;
    private readonly TestState _testState;
    
    private IConnection? _connection;
    private IModel? _channel;
    
    // Throughput tracking
    private readonly Stopwatch _stopwatch = new();
    private DateTime _lastSampleTime;
    private long _lastSampleCount;
    private int _lastLogCount;
    private DateTime _lastLogTime;
    
    private const long SamplingIntervalMs = 100;
    private const long LogIntervalMs = 1000;
    
    public async Task StartConsumingAsync(int expectedEventCount, 
        int timeoutSeconds, CancellationToken cancellationToken)
    {
        _connection = _connectionFactory.CreateConnection();
        _channel = _connection.CreateModel();
        
        var exchangeName = _configuration["RabbitMq:Exchange"];
        var consumerQueue = _configuration["RabbitMq:ConsumerQueue"];
        var consumeEventType = _configuration["RabbitMq:ConsumeEventType"];
        var prefetchCount = int.Parse(_configuration["RabbitMq:PrefetchCount"] ?? "100");
        
        // Declare and bind
        _channel.ExchangeDeclare(exchangeName, ExchangeType.Topic, durable: true);
        _channel.QueueDeclare(consumerQueue, durable: true);
        _channel.QueueBind(exchangeName, consumerQueue, consumeEventType);
        
        _channel.BasicQos(0, (ushort)prefetchCount, false);
        
        _stopwatch.Start();
        _lastSampleTime = DateTime.UtcNow;
        _lastSampleCount = 0;
        _lastLogTime = DateTime.UtcNow;
        _lastLogCount = 0;
        
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (model, ea) =>
        {
            try
            {
                await HandleMessageAsync(ea, expectedEventCount, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
            }
        };
        
        _channel.BasicConsume(consumerQueue, false, consumer);
        
        // Wait for completion
        var consumeTask = WaitForCompletionAsync(expectedEventCount, 
            timeoutSeconds, cancellationToken);
        await consumeTask;
    }
    
    private async Task HandleMessageAsync(BasicDeliverEventArgs ea, 
        int expectedEventCount, CancellationToken cancellationToken)
    {
        _testState.IncrementReceivedEventCount();
        var count = _testState.ReceivedEventCount;
        
        var currentTime = DateTime.UtcNow;
        
        // Sample throughput every 100ms
        var msSinceSample = (currentTime - _lastSampleTime).TotalMilliseconds;
        if (msSinceSample >= SamplingIntervalMs)
        {
            var eventsInPeriod = count - _lastSampleCount;
            var elapsedSinceSample = (currentTime - _lastSampleTime).TotalSeconds;
            var eventsPerSec = eventsInPeriod / elapsedSinceSample;
            
            _testState.ThroughputSamples.Add(new ThroughputSample
            {
                Timestamp = currentTime,
                ElapsedSeconds = _stopwatch.Elapsed.TotalSeconds,
                TotalCount = count,
                RatePerSecond = eventsPerSec
            });
            
            _lastSampleTime = currentTime;
            _lastSampleCount = count;
        }
        
        // Log progress every 1 second
        var msSinceLog = (currentTime - _lastLogTime).TotalMilliseconds;
        if (count == 1 || msSinceLog >= LogIntervalMs || count >= expectedEventCount)
        {
            if (count == 1)
            {
                _logger.LogInformation("Started receiving events...");
            }
            else
            {
                var eventsInPeriod = count - _lastLogCount;
                var elapsedSincelog = (currentTime - _lastLogTime).TotalSeconds;
                var eventsPerSec = eventsInPeriod / elapsedSincelog;
                _logger.LogInformation(
                    "Consuming ({Elapsed}s): {Count}/{Expected} events ({Rate} events/sec)",
                    _stopwatch.Elapsed.TotalSeconds, count, expectedEventCount, eventsPerSec);
            }
            
            _lastLogTime = currentTime;
            _lastLogCount = (int)count;
        }
        
        // Acknowledge message
        _channel?.BasicAck(ea.DeliveryTag, false);
        
        // Stop when done
        if (count >= expectedEventCount)
        {
            _channel?.BasicCancel(ea.ConsumerTag);
        }
    }
    
    private async Task WaitForCompletionAsync(int expectedEventCount, 
        int timeoutSeconds, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        
        while (DateTime.UtcNow < deadline)
        {
            if (_testState.ReceivedEventCount >= expectedEventCount)
                return;
            
            await Task.Delay(100, cancellationToken);
        }
        
        var finalCount = _testState.ReceivedEventCount;
        _logger.LogWarning(
            "Timeout: Only received {Count}/{Expected} events after {Timeout}s",
            finalCount, expectedEventCount, timeoutSeconds);
    }
    
    public void Dispose()
    {
        _stopwatch.Stop();
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
```

### Key Implementation Details

1. **AsyncEventingBasicConsumer**: Async-capable alternative to EventingBasicConsumer
   - `Received` event fires for each message
   - Handler is async (matches async/await patterns)

2. **BasicAck**: Manual acknowledgment after processing
   - Confirms message is processed
   - Requeue=false means message is discarded on failure

3. **Sampling Logic**: Same as Python but using TimeSpan
   ```csharp
   var msSinceSample = (currentTime - _lastSampleTime).TotalMilliseconds;
   if (msSinceSample >= SamplingIntervalMs)
   ```

4. **Thread Safety**: Using `TestState.IncrementReceivedEventCount()` with lock

---

## 4. Test Orchestrator Service

### BackgroundService Implementation

```csharp
public class TestOrchestratorService : BackgroundService
{
    private readonly ILogger<TestOrchestratorService> _logger;
    private readonly TestState _testState;
    private readonly TestConfiguration _config;
    private readonly IEventPublisher _publisher;
    private readonly IEventConsumer _consumer;
    private readonly IApiTester _apiTester;
    private readonly IHostApplicationLifetime _appLifetime;
    
    private Dictionary<string, DateTime?> _phaseTimestamps = new();
    private DateTime _totalStartTime;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _totalStartTime = DateTime.UtcNow;
            InitializePhaseTimestamps();
            
            _logger.LogInformation("=" * 80);
            _logger.LogInformation("SERVICE TESTER STARTED");
            _logger.LogInformation("=" * 80);
            _logger.LogInformation(
                "Configuration: {Events} events, API test for {Duration} ({Workers} virtual users)",
                _config.NumEvents, _config.ApiDuration, _config.ApiConcurrentWorkers);
            
            // Wait for service to be ready
            if (!await WaitForServiceOnPortAsync(_config.ApiPort, stoppingToken))
            {
                _appLifetime.StopApplication();
                return;
            }
            
            // Clear database and RabbitMQ
            await ClearDatabaseAsync();
            await ClearRabbitMqAsync();
            
            // Phase 0: Warmup
            await ExecuteWarmupPhaseAsync(stoppingToken);
            
            // Phase 1: Publish
            var publishTime = await ExecutePublishPhaseAsync(stoppingToken);
            
            // Phase 2: Consume
            var consumeTime = await ExecuteConsumePhaseAsync(stoppingToken);
            
            // Phase 3: API Load Test
            var (apiTime, apiSuccess, apiErrors) = 
                await ExecuteApiPhaseAsync(stoppingToken);
            
            // Summary
            LogTestSummary(publishTime, consumeTime, apiTime);
            
            // Generate reports
            await GenerateReportsAsync();
            
            _appLifetime.StopApplication();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test orchestration failed");
            _appLifetime.StopApplication();
        }
    }
    
    // Phase 0: Warmup (200 events, 5s API test, not measured)
    private async Task ExecuteWarmupPhaseAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("=" * 80);
        _logger.LogInformation("PHASE 0: WARMUP (not included in performance metrics)");
        _logger.LogInformation("=" * 80);
        
        try
        {
            // Publish warmup events
            _logger.LogInformation("Publishing {Count} warmup events...", _config.WarmupEvents);
            var warmupStartTime = DateTime.UtcNow;
            
            // Consumer in background task
            var consumerTask = Task.Run(async () =>
            {
                using var consumer = new RabbitMqEventConsumer(_logger, _testState, _config);
                await consumer.StartConsumingAsync(
                    _config.WarmupEvents, 30, cancellationToken);
            });
            
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken); // Consumer setup delay
            
            // Publish warmup events
            await _publisher.PublishEventsAsync(
                _config.WarmupEvents, TimeSpan.Zero, cancellationToken);
            
            // Wait for consumption
            _logger.LogInformation("Consuming warmup events...");
            var consumeStartTime = DateTime.UtcNow;
            while ((DateTime.UtcNow - consumeStartTime).TotalSeconds < 30)
            {
                if (_testState.ReceivedEventCount >= _config.WarmupEvents)
                    break;
                await Task.Delay(100, cancellationToken);
            }
            
            _logger.LogInformation(
                "Warmup completed in {Elapsed}s", 
                (DateTime.UtcNow - warmupStartTime).TotalSeconds);
            
            // Run warmup API test
            _logger.LogInformation("Warming up API with {Duration} load test...", 
                _config.WarmupApiDuration);
            await _apiTester.RunLoadTestAsync(
                _config.ApiUrl, _config.WarmupApiDuration, 
                _config.ApiConcurrentWorkers, cancellationToken);
            
            // Clean up and reset
            await ClearDatabaseAsync();
            await ClearRabbitMqAsync();
            _testState.ResetReceivedEventCount();
            _testState.ClearSamples();
            
            _logger.LogInformation("Warmup phase completed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Warmup phase failed - continuing anyway");
            _testState.ResetReceivedEventCount();
            _testState.ClearSamples();
        }
    }
    
    // Phase 1: Publish events
    private async Task<TimeSpan> ExecutePublishPhaseAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("=" * 80);
        _logger.LogInformation("PHASE 1: PUBLISHING EVENTS");
        _logger.LogInformation("=" * 80);
        
        _phaseTimestamps["phase1_start"] = DateTime.UtcNow - _totalStartTime;
        
        // Start consumer in background
        var consumerTask = Task.Run(async () =>
        {
            using var consumer = new RabbitMqEventConsumer(_logger, _testState, _config);
            await consumer.StartConsumingAsync(
                _config.NumEvents, 120, cancellationToken);
        }, cancellationToken);
        
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken); // Consumer setup
        
        // Publish events
        var publishStart = DateTime.UtcNow;
        await _publisher.PublishEventsAsync(
            _config.NumEvents, TimeSpan.Zero, cancellationToken);
        var publishTime = DateTime.UtcNow - publishStart;
        
        _phaseTimestamps["phase1_end"] = DateTime.UtcNow - _totalStartTime;
        
        return publishTime;
    }
    
    // Phase 2: Wait for consumption
    private async Task<TimeSpan> ExecuteConsumePhaseAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("=" * 80);
        _logger.LogInformation("PHASE 2: WAITING FOR EVENTS");
        _logger.LogInformation("=" * 80);
        
        _phaseTimestamps["phase2_start"] = DateTime.UtcNow - _totalStartTime;
        
        var consumeStart = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(120);
        
        while ((DateTime.UtcNow - consumeStart) < timeout)
        {
            if (_testState.ReceivedEventCount >= _config.NumEvents)
                break;
            
            await Task.Delay(100, cancellationToken);
        }
        
        var consumeTime = DateTime.UtcNow - consumeStart;
        _phaseTimestamps["phase2_end"] = DateTime.UtcNow - _totalStartTime;
        
        _logger.LogInformation(
            "Received {Count}/{Expected} events in {Elapsed}s",
            _testState.ReceivedEventCount, _config.NumEvents, consumeTime.TotalSeconds);
        
        return consumeTime;
    }
    
    // Phase 3: API load test
    private async Task<(TimeSpan duration, int success, int errors)> 
        ExecuteApiPhaseAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("=" * 80);
        _logger.LogInformation("PHASE 3: CALLING API");
        _logger.LogInformation("=" * 80);
        
        _phaseTimestamps["phase3_start"] = DateTime.UtcNow - _totalStartTime;
        
        var (duration, success, errors) = await _apiTester.RunLoadTestAsync(
            _config.ApiUrl, _config.ApiDuration, 
            _config.ApiConcurrentWorkers, cancellationToken);
        
        _phaseTimestamps["phase3_end"] = DateTime.UtcNow - _totalStartTime;
        
        return (duration, success, errors);
    }
    
    private async Task WaitForServiceOnPortAsync(int port, CancellationToken cancellationToken)
    {
        // Implementation of port detection (Windows: netstat, Linux: lsof)
        // See ProcessUtilities.cs for platform-specific code
        return true;
    }
    
    private async Task ClearDatabaseAsync() { /* ... */ }
    private async Task ClearRabbitMqAsync() { /* ... */ }
    private void LogTestSummary(TimeSpan pub, TimeSpan cons, TimeSpan api) { /* ... */ }
    private async Task GenerateReportsAsync() { /* ... */ }
}
```

### Key Implementation Details

1. **BackgroundService Pattern**:
   - Inherit from `BackgroundService`
   - Override `ExecuteAsync(CancellationToken)`
   - Call `_appLifetime.StopApplication()` when done

2. **Phase Timestamps**:
   - Record relative to `_totalStartTime`
   - Same format as Python: `"phase1_start"`, `"phase1_end"`, etc.
   - Value: `DateTime.UtcNow - _totalStartTime` = TimeSpan

3. **Concurrent Consumer**:
   - Start consumer with `Task.Run()` in background
   - Don't wait for it to complete immediately
   - Wait for event count in main thread instead

4. **Timeout Handling**:
   - Use `TimeSpan` for durations
   - Loop with `DateTime.UtcNow < deadline` check
   - 100ms polling interval (matches Python)

---

## 5. API Load Testing

### k6 Execution (same as Python)

```csharp
public class ApiTesterService : IApiTester
{
    private readonly ILogger<ApiTesterService> _logger;
    private readonly TestState _testState;
    private readonly TestConfiguration _config;
    
    public async Task<(TimeSpan duration, int successCount, int errorCount)> 
        RunLoadTestAsync(string apiUrl, string duration, int virtualUsers, 
        CancellationToken cancellationToken)
    {
        // Check if k6 is installed
        if (!await IsK6InstalledAsync())
        {
            _logger.LogError("k6 is not installed");
            return (TimeSpan.Zero, 0, 0);
        }
        
        // Build k6 command
        var k6Args = new[]
        {
            "run",
            "--vus", virtualUsers.ToString(),
            "--duration", duration,
            "--env", $"API_URL={apiUrl}",
            Path.Combine(AppContext.BaseDirectory, "api-load-test.js")
        };
        
        _logger.LogInformation("Starting k6: {Command}", string.Join(" ", k6Args));
        
        var startTime = DateTime.UtcNow;
        var outputFile = Path.Combine(
            AppContext.BaseDirectory, 
            $"k6-output-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.txt");
        
        try
        {
            // Run k6 process
            var psi = new ProcessStartInfo
            {
                FileName = "k6",
                Arguments = string.Join(" ", k6Args),
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
                {
                    output.AppendLine(e.Data);
                    _logger.LogDebug(e.Data);
                }
            };
            process.BeginOutputReadLine();
            
            var completed = process.WaitForExit(
                (int)ParseDurationToSeconds(duration).TotalMilliseconds + 30000);
            
            if (!completed)
            {
                process.Kill();
                _logger.LogError("k6 timed out");
                return (TimeSpan.Zero, 0, 0);
            }
            
            var elapsed = DateTime.UtcNow - startTime;
            var fullOutput = output.ToString();
            
            // Parse k6 output
            var (totalRequests, successCount, errorCount) = 
                ParseK6Output(fullOutput, startTime, 
                ParseDurationToSeconds(duration).TotalSeconds);
            
            _logger.LogInformation(
                "k6 completed: {Total} requests in {Elapsed}s ({Rate} req/s)",
                totalRequests, elapsed.TotalSeconds, 
                totalRequests / elapsed.TotalSeconds);
            
            return (elapsed, successCount, errorCount);
        }
        finally
        {
            if (File.Exists(outputFile))
                File.Delete(outputFile);
        }
    }
    
    private (int totalRequests, int successCount, int errorCount) 
        ParseK6Output(string output, DateTime startTime, double timeoutSeconds)
    {
        int totalRequests = 0;
        int successCount = 0;
        int errorCount = 0;
        
        // Parse final summary
        var match = Regex.Match(output, @"http_reqs[.\s]*:\s*(\d+)");
        if (match.Success)
            totalRequests = int.Parse(match.Groups[1].Value);
        
        // Parse success rate
        match = Regex.Match(
            output, 
            @"checks_succeeded[.\s]*:\s*([\d.]+)%\s*(\d+)\s*out of\s*(\d+)");
        if (match.Success)
        {
            successCount = int.Parse(match.Groups[2].Value);
            int totalChecks = int.Parse(match.Groups[3].Value);
            errorCount = totalChecks - successCount;
        }
        else
        {
            successCount = totalRequests;
            errorCount = 0;
        }
        
        // Parse progress samples for throughput
        var progressPattern = new Regex(
            @"running \((\d+\.\d+)s\).*?(\d+) complete.*?(\d+) interrupted");
        
        var progressData = new List<(double elapsedSec, int iterations)>();
        foreach (var line in output.Split('\n'))
        {
            match = progressPattern.Match(line);
            if (match.Success)
            {
                var elapsed = double.Parse(match.Groups[1].Value);
                var iterations = int.Parse(match.Groups[2].Value);
                progressData.Add((elapsed, iterations));
            }
        }
        
        // Generate samples
        if (!progressData.Any() && totalRequests > 0)
        {
            // Interpolate samples
            for (int sec = 1; sec <= (int)timeoutSeconds; sec++)
            {
                var estimated = (int)((totalRequests * sec) / timeoutSeconds);
                progressData.Add((sec, estimated));
            }
        }
        
        // Add to test state
        for (int i = 0; i < progressData.Count; i++)
        {
            var (elapsed, iterations) = progressData[i];
            
            double callsPerSec;
            if (i == 0)
                callsPerSec = iterations / elapsed;
            else
                callsPerSec = (iterations - progressData[i - 1].iterations) / 
                             (elapsed - progressData[i - 1].elapsedSec);
            
            _testState.ApiThroughputSamples.Add(new ThroughputSample
            {
                Timestamp = startTime.AddSeconds(elapsed),
                ElapsedSeconds = elapsed,
                TotalCount = iterations,
                RatePerSecond = callsPerSec
            });
        }
        
        return (totalRequests, successCount, errorCount);
    }
    
    private TimeSpan ParseDurationToSeconds(string duration)
    {
        // Parse "30s", "5m", "2h"
        if (duration.EndsWith("s"))
            return TimeSpan.FromSeconds(int.Parse(duration[..^1]));
        if (duration.EndsWith("m"))
            return TimeSpan.FromMinutes(int.Parse(duration[..^1]));
        if (duration.EndsWith("h"))
            return TimeSpan.FromHours(int.Parse(duration[..^1]));
        return TimeSpan.FromSeconds(30);
    }
    
    private async Task<bool> IsK6InstalledAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "k6",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            return process?.WaitForExit(2000) == true && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
```

---

## 6. Process Monitoring Service

### Implementation

```csharp
public class ProcessMonitorService : BackgroundService
{
    private readonly ILogger<ProcessMonitorService> _logger;
    private readonly TestState _testState;
    private readonly TestConfiguration _config;
    
    private Process? _targetProcess;
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _ramCounter;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Find process on port
        _targetProcess = FindProcessOnPort(_config.ApiPort);
        if (_targetProcess == null)
        {
            _logger.LogWarning("No process found on port {Port}", _config.ApiPort);
            return;
        }
        
        _logger.LogInformation(
            "Monitoring {Process} (PID: {Pid}) on port {Port}",
            _targetProcess.ProcessName, _targetProcess.Id, _config.ApiPort);
        
        try
        {
            _cpuCounter = new PerformanceCounter(
                "Process", "% Processor Time", 
                _targetProcess.ProcessName, readOnly: true);
            
            _ramCounter = new PerformanceCounter(
                "Process", "Working Set", 
                _targetProcess.ProcessName, readOnly: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not initialize performance counters");
        }
        
        var samplingInterval = TimeSpan.FromMilliseconds(_config.ServiceMonitoringIntervalMs);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cpu = _cpuCounter?.NextValue() ?? 0;
                var ram = _ramCounter?.NextValue() ?? 0; // in bytes
                var threads = _targetProcess.Threads.Count;
                
                // Record sample (not used in main test, but collected for reports)
                // Store in background collection if needed
                
                await Task.Delay(samplingInterval, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error sampling process metrics");
            }
        }
    }
    
    private Process? FindProcessOnPort(int port)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var netstatOutput = RunCommand("netstat", "-ano");
                var lines = netstatOutput.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains($":{port}") && line.Contains("LISTENING"))
                    {
                        var parts = line.Split(new[] { ' ' }, 
                            StringSplitOptions.RemoveEmptyEntries);
                        if (int.TryParse(parts[^1], out int pid))
                            return Process.GetProcessById(pid);
                    }
                }
            }
            else // Linux/macOS
            {
                var lsofOutput = RunCommand("lsof", $"-ti :{port}");
                if (!string.IsNullOrWhiteSpace(lsofOutput))
                {
                    var pid = int.Parse(lsofOutput.Trim().Split('\n')[0]);
                    return Process.GetProcessById(pid);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error finding process on port {Port}", port);
        }
        
        return null;
    }
    
    private string RunCommand(string command, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {command}");
        
        process.WaitForExit(2000);
        return process.StandardOutput.ReadToEnd();
    }
}
```

---

## 7. Report Generation Service

### JSON Report Structure

```csharp
public class TestReport
{
    [JsonPropertyName("test_date")]
    public string TestDate { get; set; }
    
    [JsonPropertyName("total_runtime_seconds")]
    public double TotalRuntimeSeconds { get; set; }
    
    [JsonPropertyName("system")]
    public Dictionary<string, object> SystemInfo { get; set; }
    
    [JsonPropertyName("phase_timestamps")]
    public Dictionary<string, double?> PhaseTimestamps { get; set; }
    
    [JsonPropertyName("monitored_process")]
    public ProcessInfo? MonitoredProcess { get; set; }
    
    [JsonPropertyName("configuration")]
    public TestConfiguration Configuration { get; set; }
    
    [JsonPropertyName("results")]
    public TestResults Results { get; set; }
}

public class ProcessInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
    
    [JsonPropertyName("pid")]
    public int Pid { get; set; }
    
    [JsonPropertyName("port")]
    public int Port { get; set; }
}

public class TestResults
{
    [JsonPropertyName("phase1_publish")]
    public PhaseResult Phase1Publish { get; set; }
    
    [JsonPropertyName("phase2_consume")]
    public PhaseResult Phase2Consume { get; set; }
    
    [JsonPropertyName("phase3_api")]
    public ApiPhaseResult Phase3Api { get; set; }
}

public class PhaseResult
{
    [JsonPropertyName("duration_seconds")]
    public double DurationSeconds { get; set; }
    
    [JsonPropertyName("throughput_events_per_sec")]
    public double ThroughputPerSec { get; set; }
}

public class ApiPhaseResult : PhaseResult
{
    [JsonPropertyName("total_requests")]
    public int TotalRequests { get; set; }
    
    [JsonPropertyName("throughput_calls_per_sec")]
    public double ThroughputCallsPerSec { get; set; }
    
    [JsonPropertyName("success_count")]
    public int SuccessCount { get; set; }
    
    [JsonPropertyName("success_percentage")]
    public double SuccessPercentage { get; set; }
    
    [JsonPropertyName("error_count")]
    public int ErrorCount { get; set; }
    
    [JsonPropertyName("error_percentage")]
    public double ErrorPercentage { get; set; }
}
```

### Report Generation Implementation

```csharp
public class ReportGeneratorService : IReportGenerator
{
    private readonly ILogger<ReportGeneratorService> _logger;
    
    public async Task GenerateReportAsync(
        string outputPath,
        TestState testState,
        TestConfiguration config,
        Dictionary<string, DateTime?> phaseTimestamps,
        TimeSpan publishTime,
        TimeSpan consumeTime,
        TimeSpan apiTime,
        int apiSuccess,
        int apiErrors)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var reportPath = Path.Combine(
            outputPath, 
            $"test-report-{timestamp}.json");
        
        var report = new TestReport
        {
            TestDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            TotalRuntimeSeconds = publishTime.TotalSeconds + 
                                 consumeTime.TotalSeconds + 
                                 apiTime.TotalSeconds,
            SystemInfo = GetSystemInfo(),
            PhaseTimestamps = phaseTimestamps.ToDictionary(
                k => k.Key,
                v => v.Value?.TotalSeconds),
            Configuration = config,
            Results = new TestResults
            {
                Phase1Publish = new PhaseResult
                {
                    DurationSeconds = publishTime.TotalSeconds,
                    ThroughputPerSec = config.NumEvents / publishTime.TotalSeconds
                },
                Phase2Consume = new PhaseResult
                {
                    DurationSeconds = consumeTime.TotalSeconds,
                    ThroughputPerSec = config.NumEvents / consumeTime.TotalSeconds
                },
                Phase3Api = new ApiPhaseResult
                {
                    DurationSeconds = apiTime.TotalSeconds,
                    TotalRequests = apiSuccess + apiErrors,
                    ThroughputCallsPerSec = (apiSuccess + apiErrors) / apiTime.TotalSeconds,
                    SuccessCount = apiSuccess,
                    SuccessPercentage = (apiSuccess * 100.0) / (apiSuccess + apiErrors),
                    ErrorCount = apiErrors,
                    ErrorPercentage = (apiErrors * 100.0) / (apiSuccess + apiErrors)
                }
            }
        };
        
        var json = JsonSerializer.Serialize(report, 
            new JsonSerializerOptions { WriteIndented = true });
        
        await File.WriteAllTextAsync(reportPath, json);
        _logger.LogInformation("Report written to {Path}", reportPath);
    }
    
    public async Task GenerateThroughputReportAsync(
        string outputPath,
        IEnumerable<ThroughputSample> samples,
        string metricName)
    {
        if (!samples.Any())
        {
            _logger.LogWarning("No {MetricName} throughput samples collected", metricName);
            return;
        }
        
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var reportPath = Path.Combine(
            outputPath,
            $"test-report-{timestamp}-{metricName}-throughput.json");
        
        var summary = CalculateSummary(samples, metricName);
        
        var report = new
        {
            test_date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            sampling_interval_ms = 100,
            samples = samples.OrderBy(s => s.Timestamp).ToList(),
            summary = summary
        };
        
        var json = JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true });
        
        await File.WriteAllTextAsync(reportPath, json);
        _logger.LogInformation("Throughput report written to {Path}", reportPath);
    }
    
    private Dictionary<string, object> CalculateSummary(
        IEnumerable<ThroughputSample> samples,
        string metricName)
    {
        var sampleList = samples.ToList();
        var rates = sampleList.Select(s => s.RatePerSecond).ToList();
        var nonZeroRates = rates.Where(r => r > 0).ToList();
        
        var avgRate = rates.Average();
        var stdDev = CalculateStdDev(rates);
        var cv = stdDev / avgRate * 100;
        
        return new Dictionary<string, object>
        {
            [$"avg_{metricName}_per_second"] = Math.Round(avgRate, 2),
            [$"peak_{metricName}_per_second"] = Math.Round(rates.Max(), 2),
            [$"min_{metricName}_per_second"] = 
                Math.Round(nonZeroRates.Any() ? nonZeroRates.Min() : 0, 2),
            [$"std_dev_{metricName}_per_second"] = Math.Round(stdDev, 2),
            [$"cv_{metricName}_per_second"] = Math.Round(cv, 2),
            ["avg_response_time_ms"] = Math.Round(1000.0 / avgRate, 3),
            ["total_samples"] = sampleList.Count,
            [$"total_{metricName}"] = sampleList.Last().TotalCount
        };
    }
    
    private double CalculateStdDev(List<double> values)
    {
        if (values.Count < 2)
            return 0;
        
        var mean = values.Average();
        var sumOfSquares = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sumOfSquares / (values.Count - 1));
    }
    
    private Dictionary<string, object> GetSystemInfo()
    {
        return new Dictionary<string, object>
        {
            ["os"] = RuntimeInformation.OSDescription,
            ["processor_count"] = Environment.ProcessorCount,
            ["dotnet_version"] = RuntimeInformation.FrameworkDescription,
            ["total_memory_gb"] = 
                GC.GetTotalMemory(false) / (1024.0 * 1024.0 * 1024.0)
        };
    }
}
```

---

## 8. Program Setup (Dependency Injection & IHost)

```csharp
// Program.cs
var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;
        
        // Configuration
        var testConfig = new TestConfiguration
        {
            NumEvents = int.Parse(config["Testing:NumEvents"] ?? "10000"),
            ApiDuration = config["Testing:ApiDuration"] ?? "30s",
            ApiConcurrentWorkers = 
                int.Parse(config["Testing:ApiConcurrentWorkers"] ?? "1"),
            ApiPort = int.Parse(config["Testing:ApiPort"] ?? "8080"),
            ApiUrl = config["Testing:ApiUrl"] ?? "http://localhost:8080/kpi",
            WarmupEvents = int.Parse(config["Testing:WarmupEvents"] ?? "200"),
            WarmupApiDuration = config["Testing:WarmupApiDuration"] ?? "5s",
            ServiceMonitoringIntervalMs = 
                int.Parse(config["Monitoring:ServiceIntervalMs"] ?? "500"),
            ContainerMonitoringIntervalMs = 
                int.Parse(config["Monitoring:ContainerIntervalMs"] ?? "3000"),
            ResultsFolder = config["Testing:ResultsFolder"] ?? "./test-results"
        };
        services.AddSingleton(testConfig);
        
        // Core services
        services.AddSingleton<TestState>();
        services.AddSingleton<IConnectionFactory>(sp =>
            new ConnectionFactory
            {
                HostName = config["RabbitMq:Host"] ?? "localhost",
                Port = int.Parse(config["RabbitMq:Port"] ?? "5672"),
                UserName = config["RabbitMq:User"] ?? "admin",
                Password = config["RabbitMq:Pass"] ?? "admin"
            });
        
        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
        services.AddSingleton<IEventConsumer, RabbitMqEventConsumer>();
        services.AddSingleton<IApiTester, ApiTesterService>();
        services.AddSingleton<IReportGenerator, ReportGeneratorService>();
        
        // Background services
        services.AddHostedService<TestOrchestratorService>();
        services.AddHostedService<ProcessMonitorService>();
        // Add other monitoring services...
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.AddDebug();
    })
    .Build();

await builder.RunAsync();
```

---

## Critical Implementation Notes

### 1. Thread Safety
- Use `lock` for counters (not collections)
- Use `ConcurrentBag<T>` for thread-safe lists
- Consumer callback runs on RabbitMQ's thread pool (async)
- Main thread reads counters with lock

### 2. Async/Await Patterns
- All I/O should be async (RabbitMQ, file, process)
- Use `async Task` for BackgroundService implementations
- Avoid `.Wait()` or `.Result` (can cause deadlocks)
- Propagate `CancellationToken` throughout

### 3. Process Management
- Use `Process.Start()` for running k6
- Capture output with `OutputDataReceived` event
- Always dispose `Process` and `StreamReader` objects
- Handle process exit gracefully (timeout, kill)

### 4. Timing Precision
- Use `DateTime.UtcNow` for all timestamps
- Use `Stopwatch` for elapsed time measurements
- Round to 3 decimal places for seconds (matching Python)
- Store all durations as `TimeSpan` for type safety

### 5. Configuration Management
- Read from environment variables (like Python)
- Provide sensible defaults
- Use IConfiguration for structured access
- Store in `TestConfiguration` class for dependency injection

### 6. Error Handling
- Catch and log exceptions (don't crash)
- Warmup failures should not stop main test
- Database/RabbitMQ cleanup should retry
- Missing k6 should be handled gracefully (return zeros)

### 7. Testing Strategy (for .NET)
- Unit test metric calculation (summary statistics)
- Integration test with mock RabbitMQ
- End-to-end test with real containers
- Test concurrent phases with Task.Run()

---

## Next Steps for Implementation

1. Create project structure with all service interfaces
2. Implement TestState and configuration models
3. Implement RabbitMQ publisher and consumer
4. Implement TestOrchestratorService (main logic)
5. Implement API tester (k6 execution)
6. Implement monitoring services
7. Implement report generation
8. Add CLI argument parsing
9. Create comprehensive unit tests
10. Document DevContainers setup for .NET
