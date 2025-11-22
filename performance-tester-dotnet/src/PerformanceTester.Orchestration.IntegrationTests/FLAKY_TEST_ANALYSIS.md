# Flaky Test Analysis: `RunTestAsync_WhenConsumerTimeout_ShouldIncludeReceivedCount`

**Date:** 2025-01-21
**Status:** ✅ FIXED
**Test File:** `OrchestratorErrorHandlingTests.cs:49-106`

## Symptoms

- Test passes when run first or in isolation
- Test fails intermittently when run after other tests (especially `OrchestratorCompleteWorkflowTests`)
- Failure message: `"Inactivity timeout expired: No events received"` (0/2)
- Expected message: `"1/2"` (received 1 out of 2)

## Root Cause: Stale Events in Durable Queue

The test fails because **stale events from previous tests accumulate in the `configurablereferenceservice-input` queue** and cause premature triggering.

### Key Insight

The queue `configurablereferenceservice-input` is **durable** (`durable: true`, `autoDelete: false`) and bound to the same exchange and routing key used by all services. This means:

1. Events published to exchange `referenceservice.comparison` with routing key `instrument.status.changed` are routed to **ALL bound queues**
2. Even when no consumer is connected, the queue exists and accumulates events
3. The queue persists across test sessions (survives container restarts in some cases)

### Why DotNetAotService Tests Don't Have This Problem

The `OrchestratorCompleteWorkflowTests` uses the real `.NET AOT service`, which:
- Processes all events it receives
- Doesn't have a "trigger after N events" mechanism
- Uses a separate queue (`dotnet9aotreferenceservice-input`)

In contrast, `ConfigurableReferenceService`:
- Counts ALL received events via `_receivedInputEventCount`
- Triggers publication after `warmupEventCount + eventIndexToTrigger` events
- Cannot distinguish between "stale events from previous test" and "fresh events from current test"

### Sequence of Events (Failing Case)

```
BACKGROUND: Previous test run (e.g., OrchestratorCompleteWorkflowTests)
  └─ Published 500 events to exchange "referenceservice.comparison"
     └─ Events routed to ALL bound queues, including "configurablereferenceservice-input"
     └─ Queue now contains 500 stale events (persisted, never consumed)

STEP 1: Test starts (OrchestratorErrorHandlingTests.cs:49)
  └─ ConfigurableReferenceService.ConfigurePublication(eventCount: 1, warmupEventCount: 1)
     └─ _eventIndexToTrigger = 1  (triggers on 2nd event after warmup, i.e., event #2 overall)
     └─ _warmupEventCount = 1

STEP 2: Connect to RabbitMQ (OrchestratorErrorHandlingTests.cs:82)
  └─ ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: 9999)
     └─ Creates/binds queue "configurablereferenceservice-input"
     └─ Subscribes as consumer
     └─ ⚠️ IMMEDIATELY receives 500 stale events from queue!

STEP 3: Processing stale events
  └─ Event #1: _receivedInputEventCount = 1 (warmup, logged)
  └─ Event #2: _receivedInputEventCount = 2 → TRIGGERS! Publishes 1 KPI event
  └─ Events #3-500: _receivedInputEventCount = 3-500 (logged as "Ignoring input event N (already triggered)")

STEP 4: RunTestAsync → ExecuteSetupPhaseAsync (TestOrchestrator.cs:232)
  └─ ClearAllQueuesAsync() ← ⚠️ PURGES THE KPI EVENT THAT WAS JUST PUBLISHED!

STEP 5: Warmup phase runs
  └─ Publishes 1 warmup event
  └─ ConfigurableReferenceService sees _receivedInputEventCount = 501 → ignores (already triggered)

STEP 6: Test phase runs
  └─ Publishes 2 test events
  └─ ConfigurableReferenceService sees _receivedInputEventCount = 502, 503 → ignores both
  └─ No KPI events published (service already triggered during stale event processing)

STEP 7: Consumer times out
  └─ Expected 2 KPI events, received 0
  └─ Throws: "Inactivity timeout expired: No events received" (0/2)

RESULT: "No events received" (0/2)
EXPECTED: "1/2" (should receive 1 out of 2 expected events)
```

### Evidence from Test Output

When the test fails, the output shows:
```
⊙ Ignoring input event 488 (already triggered)
⊙ Ignoring input event 489 (already triggered)
...
⊙ Ignoring input event 500 (already triggered)
```

This proves that ~500 stale events from `OrchestratorCompleteWorkflowTests` (which publishes 500 events) were present in the queue.

## The Fix: Queue Purging in All Tests

### Solution Implemented

All orchestration integration tests now purge the `configurablereferenceservice-input` queue:

1. **All tests** - Purge in `finally` block to clean up after the test
2. **The flaky test** - Also purges **BEFORE** connecting to ensure clean state

### Code Changes

**Added constant to ConfigurableReferenceService.cs:**
```csharp
/// <summary>
/// Default queue name for ConfigurableReferenceService input events.
/// Tests should purge this queue at the end to prevent stale event accumulation.
/// </summary>
public const string DefaultInputQueueName = "configurablereferenceservice-input";
```

**All test files updated with try/finally:**
```csharp
try
{
    // ... test logic ...
}
finally
{
    // CRITICAL: Purge ConfigurableReferenceService queue to prevent stale events
    // from affecting subsequent tests. Events published to the shared exchange
    // are routed to ALL bound queues, including this one if it exists.
    await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
}
```

**The flaky test (WhenConsumerTimeout) also purges BEFORE connecting:**
```csharp
try
{
    // CRITICAL: Purge the queue BEFORE connecting to remove stale events from previous tests
    await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);

    // Make ConfigurableReferenceService discoverable on the test port (recreates its queue)
    await System.ConfigurableReferenceService.ConnectAndSubscribeAsync(listenPort: config.ServicePort);
    // ... rest of test ...
}
finally
{
    await System.ConfigurableReferenceService.DisconnectAsync();
    await System.RabbitMQ.PurgeQueueAsync(ConfigurableReferenceService.DefaultInputQueueName);
}
```

## Files Modified

| File | Change |
|------|--------|
| `Infrastructure/ConfigurableReferenceService.cs` | Added `DefaultInputQueueName` constant |
| `OrchestratorCompleteWorkflowTests.cs` | Added try/finally with queue purge |
| `OrchestratorCancellationTests.cs` | Added try/finally with queue purge |
| `OrchestratorErrorHandlingTests.cs` | Added try/finally with queue purge (both tests), plus purge BEFORE connect in flaky test |
| `OrchestratorSetupPhaseTests.cs` | Added try/finally with queue purge |

## Why This Fix Works

1. **Purging BEFORE connect** in the flaky test ensures no stale events are present when ConfigurableReferenceService starts consuming
2. **Purging in finally blocks** prevents the current test from leaving stale events that affect subsequent tests
3. Using a **named constant** makes it clear which queue needs purging and why

## Test Execution Order

Tests run alphabetically by class name:

1. `OrchestratorCancellationTests` - Now purges queue in finally
2. `OrchestratorCompleteWorkflowTests` - Now purges queue in finally (was the source of 500 stale events)
3. `OrchestratorErrorHandlingTests.RunTestAsync_WhenServiceNotRunning_ShouldFailGracefully` - Now purges queue in finally
4. **`OrchestratorErrorHandlingTests.RunTestAsync_WhenConsumerTimeout_ShouldIncludeReceivedCount`** - Now purges BEFORE and AFTER
5. `OrchestratorSetupPhaseTests` - Now purges queue in finally

## Lessons Learned

1. **Durable queues persist across test sessions** - Always clean up test-specific queues
2. **Topic exchanges route to ALL bound queues** - Events published to a shared exchange affect all subscribers
3. **Test isolation requires explicit cleanup** - Don't assume queues are empty at test start
4. **Real service tests don't show the problem** - The DotNetAotService doesn't have the "trigger once" mechanism, so it processes all events without issue

## Summary

The test was flaky because stale events from `OrchestratorCompleteWorkflowTests` (500 events) accumulated in the durable `configurablereferenceservice-input` queue. When `ConfigurableReferenceService` connected, it immediately processed all 500 stale events, triggered prematurely on event #2, and then the orchestrator's queue purge deleted the single KPI event that was published. The fix ensures all tests purge this queue in their finally blocks, and the flaky test also purges before connecting.
