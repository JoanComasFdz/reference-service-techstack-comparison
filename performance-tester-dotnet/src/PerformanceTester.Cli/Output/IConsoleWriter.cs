using PerformanceTester.Reporting;

namespace PerformanceTester.Cli.Output;

/// <summary>
/// Provides formatted console output for CLI commands.
/// </summary>
public interface IConsoleWriter
{
    void WriteHeader(string text);
    void WriteLine(string text = "");
    void WriteInfo(string text);
    void WriteSuccess(string text);
    void WriteWarning(string text);
    void WriteError(string text);
    void WriteConfigTable(Orchestration.TestConfiguration config);
    void WriteResultsTable(TestReport report);
}
