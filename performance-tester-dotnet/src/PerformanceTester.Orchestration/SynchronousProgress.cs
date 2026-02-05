namespace PerformanceTester.Orchestration;

/// <summary>
/// Synchronous implementation of IProgress that invokes callback immediately.
/// Unlike Progress{T}, does not post to SynchronizationContext.
/// </summary>
internal sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SynchronousProgress(Action<T> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public void Report(T value)
    {
        _handler(value);
    }
}
