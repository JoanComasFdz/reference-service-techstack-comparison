namespace PerformanceTester.Cli.Output;

/// <summary>
/// Simple progress reporter that shows a spinner during test execution.
/// </summary>
public sealed class ProgressReporter : IProgressReporter
{
    private CancellationTokenSource? _cts;
    private Task? _spinnerTask;
    private static readonly string[] SpinnerFrames = ["|", "/", "-", "\\"];

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _spinnerTask = Task.Run(async () =>
        {
            var frameIndex = 0;
            while (!_cts.Token.IsCancellationRequested)
            {
                Console.Write($"\r  {SpinnerFrames[frameIndex]} Running test...");
                frameIndex = (frameIndex + 1) % SpinnerFrames.Length;
                try
                {
                    await Task.Delay(100, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _spinnerTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch { /* Ignore */ }

        Console.Write("\r" + new string(' ', 30) + "\r"); // Clear spinner line
        _cts?.Dispose();
    }
}
