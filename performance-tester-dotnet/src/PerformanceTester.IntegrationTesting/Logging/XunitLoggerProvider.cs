using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace PerformanceTester.IntegrationTesting.Logging;

/// <summary>
/// Logger provider that creates XunitLogger instances.
/// </summary>
public sealed class XunitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;

    public XunitLoggerProvider(ITestOutputHelper output)
    {
        _output = output;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new XunitLogger(_output, categoryName);
    }

    public void Dispose() { }
}
