# 03-05. Do Not Null-Check DI-Injected Constructor Parameters

Constructors that receive services from the DI container must not guard against null with `ArgumentNullException`. The container guarantees that all registered services are non-null. A null check here is defensive code for a misconfiguration scenario that cannot occur at runtime — it adds noise and implies a doubt about the DI infrastructure that is not warranted.

```csharp
// ✅ Good — assign directly, trust the container
public ProcessMonitorBackgroundService(
    TimeSpan samplingInterval,
    ILogger<ProcessMonitorBackgroundService> logger)
{
    _samplingInterval = samplingInterval;
    _logger = logger;
}

// ❌ Avoid — redundant null guard on a DI-injected parameter
public ProcessMonitorBackgroundService(
    TimeSpan samplingInterval,
    ILogger<ProcessMonitorBackgroundService> logger)
{
    _samplingInterval = samplingInterval;
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
}
```

This rule applies to every DI-injected type: `ILogger<T>`, named delegates, hosted services, options classes, and any other service resolved from the container. If the container is misconfigured (e.g., a service was never registered), it throws at resolution time — before the constructor body runs — so a runtime null guard in the constructor is never reached anyway.

**Scope:** This rule covers constructor parameters resolved by DI. It does not apply to public API methods that accept nullable arguments from untrusted callers, or to factory/static methods where the caller supplies the value directly.
