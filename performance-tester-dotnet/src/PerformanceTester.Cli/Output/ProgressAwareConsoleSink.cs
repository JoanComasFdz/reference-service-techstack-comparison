using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace PerformanceTester.Cli.Output;

/// <summary>
/// Serilog sink that coordinates with progress display.
/// Clears progress bar before writing log, re-renders after.
/// </summary>
public sealed class ProgressAwareConsoleSink : ILogEventSink
{
    private readonly ITextFormatter _formatter;

    public ProgressAwareConsoleSink(string outputTemplate)
    {
        _formatter = new MessageTemplateTextFormatter(outputTemplate);
    }

    public void Emit(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        _formatter.Format(logEvent, writer);
        var message = writer.ToString().TrimEnd();

        ConsoleCoordinator.WriteLogLine(message);
    }
}
