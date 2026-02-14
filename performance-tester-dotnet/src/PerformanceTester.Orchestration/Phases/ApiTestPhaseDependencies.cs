using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PerformanceTester.ApiLoadTesting;

namespace PerformanceTester.Orchestration;

/// <summary>
/// Builds the <see cref="RunApiTest"/> delegate from DI-resolved interfaces.
/// </summary>
internal static class ApiTestPhaseDependencies
{
    public static RunApiTest Build(
        IServiceProvider services,
        TestConfiguration config,
        ILogger logger,
        CancellationToken ct)
    {
        var apiLoadTester = services.GetRequiredService<IApiLoadTester>();

        return (progress) => ApiTestPhase.ExecuteAsync(
            config,
            (url, duration, vus, apiProgress, maxFail, dir) => apiLoadTester.StartTestAsync(url, duration, vus, apiProgress, maxFail, dir, ct),
            progress,
            logger);
    }
}
