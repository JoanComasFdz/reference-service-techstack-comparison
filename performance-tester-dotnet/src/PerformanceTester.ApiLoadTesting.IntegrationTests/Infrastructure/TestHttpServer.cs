using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PerformanceTester.ApiLoadTesting.IntegrationTests.Infrastructure;

/// <summary>
/// Simple HTTP server for testing API load testing functionality.
/// Returns "Hello World" for all requests.
/// Each test should use a different port to enable parallel execution.
/// Uses Kestrel (ASP.NET Core) for reliable cross-platform HTTP serving.
/// </summary>
public sealed class TestHttpServer : IDisposable
{
    private readonly IHost _host;

    public string BaseUrl { get; }

    public TestHttpServer(int port)
    {
        BaseUrl = $"http://localhost:{port}/";

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(BaseUrl);

        // Disable all logging to keep test output clean
        builder.Logging.ClearProviders();

        var app = builder.Build();

        // Handle all requests with "Hello World"
        app.MapGet("/{**catch-all}", () => "Hello World");

        _host = app;

        // Start server and wait for it to be fully ready
        _host.StartAsync().Wait();

        // Give Kestrel an extra moment to fully bind and be ready for connections
        Thread.Sleep(500);
    }

    public void Dispose()
    {
        try
        {
            _host?.StopAsync(TimeSpan.FromSeconds(2)).Wait();
            _host?.Dispose();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
