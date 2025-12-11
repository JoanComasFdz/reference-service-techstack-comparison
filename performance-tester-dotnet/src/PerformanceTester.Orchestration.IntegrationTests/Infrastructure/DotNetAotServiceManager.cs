using System.Diagnostics;
using Xunit.Abstractions;

namespace PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

/// <summary>
/// Manages .NET 9 AOT service lifecycle for integration tests.
/// Builds, starts, and stops the .NET AOT reference service.
/// </summary>
public sealed class DotNetAotServiceManager : IDisposable
{
    private readonly ITestOutputHelper? _output;
    private readonly string _postgresHost;
    private readonly int _postgresPort;
    private readonly string _postgresUser;
    private readonly string _postgresPassword;
    private readonly string _rabbitMqHost;
    private readonly int _rabbitMqPort;
    private readonly string _rabbitMqUser;
    private readonly string _rabbitMqPassword;
    private readonly string? _rabbitMqVhost;
    private Process? _dotnetProcess;
    private const string DotNetAotServicePath = "/workspace/implementations/dotnet9aot";
    private const string DotNetAotServiceBinary = "dotnet9AotReferenceService";
    private const string DotNetAotServicePublishPath = "bin/Release/net9.0/linux-x64/publish";
    private const int DotNetAotServicePort = 8093;
    private const string DotNetAotServiceUrl = "http://localhost:8093";

    public DotNetAotServiceManager(
        string postgresHost,
        int postgresPort,
        string postgresUser,
        string postgresPassword,
        string rabbitMqHost,
        int rabbitMqPort,
        string rabbitMqUser,
        string rabbitMqPassword,
        string? rabbitMqVhost,
        ITestOutputHelper? output)
    {
        _postgresHost = postgresHost;
        _postgresPort = postgresPort;
        _postgresUser = postgresUser;
        _postgresPassword = postgresPassword;
        _rabbitMqHost = rabbitMqHost;
        _rabbitMqPort = rabbitMqPort;
        _rabbitMqUser = rabbitMqUser;
        _rabbitMqPassword = rabbitMqPassword;
        _rabbitMqVhost = rabbitMqVhost;
        _output = output;
    }

    /// <summary>
    /// Builds the .NET AOT service if the binary doesn't exist.
    /// </summary>
    public void BuildIfNeeded()
    {
        var binaryPath = Path.Combine(DotNetAotServicePath, DotNetAotServicePublishPath, DotNetAotServiceBinary);

        if (File.Exists(binaryPath))
        {
            _output?.WriteLine($".NET AOT service binary already exists: {binaryPath}");
            return;
        }

        _output?.WriteLine("Building .NET AOT service (this may take 1-3 minutes)...");

        var buildProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "publish -c Release",
                WorkingDirectory = DotNetAotServicePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        buildProcess.Start();
        var output = buildProcess.StandardOutput.ReadToEnd();
        var error = buildProcess.StandardError.ReadToEnd();
        buildProcess.WaitForExit();

        if (buildProcess.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $".NET AOT service build failed with exit code {buildProcess.ExitCode}. " +
                $"Error: {error}");
        }

        _output?.WriteLine(".NET AOT service built successfully");
    }

    /// <summary>
    /// Starts the .NET AOT service and waits for it to be healthy.
    /// </summary>
    public async Task StartAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        // Ensure binary exists
        BuildIfNeeded();

        var binaryPath = Path.Combine(DotNetAotServicePath, DotNetAotServicePublishPath, DotNetAotServiceBinary);

        _output?.WriteLine($"Starting .NET AOT service from {binaryPath}...");

        _dotnetProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                WorkingDirectory = Path.Combine(DotNetAotServicePath, DotNetAotServicePublishPath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                // Environment variables for .NET AOT service
                // Using ASP.NET Core hierarchical configuration format (double-underscore)
            }
        };

        _dotnetProcess.StartInfo.EnvironmentVariables["Postgres__Host"] = _postgresHost;
        _dotnetProcess.StartInfo.EnvironmentVariables["Postgres__Port"] = _postgresPort.ToString();
        _dotnetProcess.StartInfo.EnvironmentVariables["Postgres__Database"] = databaseName;
        _dotnetProcess.StartInfo.EnvironmentVariables["Postgres__Username"] = _postgresUser;
        _dotnetProcess.StartInfo.EnvironmentVariables["Postgres__Password"] = _postgresPassword;
        _dotnetProcess.StartInfo.EnvironmentVariables["RabbitMQ__Host"] = _rabbitMqHost;
        _dotnetProcess.StartInfo.EnvironmentVariables["RabbitMQ__Port"] = _rabbitMqPort.ToString();
        _dotnetProcess.StartInfo.EnvironmentVariables["RabbitMQ__Username"] = _rabbitMqUser;
        _dotnetProcess.StartInfo.EnvironmentVariables["RabbitMQ__Password"] = _rabbitMqPassword;
        if (!string.IsNullOrEmpty(_rabbitMqVhost))
        {
            _dotnetProcess.StartInfo.EnvironmentVariables["RabbitMQ__VirtualHost"] = _rabbitMqVhost;
        }
        _dotnetProcess.StartInfo.EnvironmentVariables["ASPNETCORE_URLS"] = DotNetAotServiceUrl;

        // Capture output for debugging
        _dotnetProcess.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                _output?.WriteLine($"[.NET AOT Service] {e.Data}");
        };
        _dotnetProcess.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                _output?.WriteLine($"[.NET AOT Service ERROR] {e.Data}");
        };

        _dotnetProcess.Start();
        _dotnetProcess.BeginOutputReadLine();
        _dotnetProcess.BeginErrorReadLine();

        // Wait for service to be healthy
        await WaitForHealthAsync(cancellationToken);

        _output?.WriteLine($".NET AOT service started successfully on {DotNetAotServiceUrl}");
    }

    /// <summary>
    /// Waits for the .NET AOT service to respond to health checks.
    /// </summary>
    private async Task WaitForHealthAsync(CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        var timeout = TimeSpan.FromSeconds(30);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                var response = await httpClient.GetAsync(
                    $"{DotNetAotServiceUrl}/kpi",
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _output?.WriteLine($".NET AOT service healthy after {stopwatch.Elapsed.TotalSeconds:F2}s");
                    return;
                }
            }
            catch
            {
                // Service not ready yet, continue polling
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException(
            $".NET AOT service did not become healthy within {timeout.TotalSeconds}s");
    }

    /// <summary>
    /// Stops the .NET AOT service.
    /// </summary>
    public void Stop()
    {
        if (_dotnetProcess == null || _dotnetProcess.HasExited)
            return;

        _output?.WriteLine("Stopping .NET AOT service...");

        try
        {
            _dotnetProcess.Kill(entireProcessTree: true);
            _dotnetProcess.WaitForExit(5000);
            _output?.WriteLine(".NET AOT service stopped");
        }
        catch (Exception ex)
        {
            _output?.WriteLine($"Error stopping .NET AOT service: {ex.Message}");
            throw;
        }
    }

    public void Dispose()
    {
        Stop();
        _dotnetProcess?.Dispose();
    }
}
