using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace PerformanceTester.IntegrationTesting.Logging;

/// <summary>
/// ILogger implementation that writes to xUnit ITestOutputHelper.
/// </summary>
internal sealed class XunitLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    public XunitLogger(ITestOutputHelper output, string categoryName)
    {
        _output = output;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}Z {logLevel}] {_categoryName}: {message}");

        if (exception != null)
        {
            _output.WriteLine($"Exception: {exception}");
        }
    }
}
