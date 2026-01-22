namespace PerformanceTester.Cli.Output;

/// <summary>
/// Reports test progress to the console.
/// </summary>
public interface IProgressReporter
{
    void Start();
    void Stop();
}
