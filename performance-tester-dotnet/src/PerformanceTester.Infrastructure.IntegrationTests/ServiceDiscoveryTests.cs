using JoanComasFdz.AssertingThat;
using PerformanceTester.Infrastructure.IntegrationTests.Infrastructure;
using PerformanceTester.Infrastructure.ValueObjects;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.Infrastructure.IntegrationTests;

public sealed class ServiceDiscoveryTests(ITestOutputHelper output) : IntegrationTest(output)
{
    [Fact]
    public async Task FindServiceProcessIdAsync_WhenServiceIsRunning_ShouldReturnProcessId()
    {
        // Arrange - Start a TCP listener on an available port
        var testPortNumber = System.OS.StartProcessOnPort(); // OS assigns available port
        var testPort = Port.FromInt(testPortNumber);

        try
        {
            // Act - Should find the current process (listener runs in this test process)
            await Asserting.That(System.Infrastructure.FindServiceProcessId)
                .FindsCurrentProcessOnPort(testPort, TimeSpan.FromSeconds(5));
        }
        finally
        {
            // Cleanup
            System.OS.StopProcessOnPort(testPortNumber);
        }
    }

    [Fact]
    public async Task FindServiceProcessIdAsync_WhenNoServiceOnPort_ShouldReturnFailure()
    {
        // Arrange - port 54321 should be unused
        var unusedPort = Port.FromInt(54321);

        // Act & Assert
        await Asserting.That(System.Infrastructure.FindServiceProcessId)
            .FindsNoProcessOnPort(unusedPort, TimeSpan.FromSeconds(2));
    }
}
