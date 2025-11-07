using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace PerformanceTester.IntegrationTesting.Logging;

/// <summary>
/// Extension methods for configuring Microsoft.Extensions.Logging with xUnit output.
/// </summary>
public static class LoggingTestExtensions
{
    /// <summary>
    /// Adds xUnit test output as a logging destination.
    /// All service logs will appear in test output.
    /// </summary>
    public static ILoggingBuilder AddXunitOutput(
        this ILoggingBuilder builder,
        ITestOutputHelper output)
    {
        builder.AddProvider(new XunitLoggerProvider(output));
        return builder;
    }
}
