using JoanComasFdz.AssertingThat;
using PerformanceTester.Infrastructure.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.Infrastructure.IntegrationTests;

public sealed class ServiceDiscoveryTests(ITestOutputHelper output) : IntegrationTest(output)
{
    [Fact]
    public async Task FindServiceProcessIdAsync_WhenServiceIsRunning_ShouldReturnProcessId()
    {
        // Arrange - Start a TCP listener on an available port
        var testPort = System.OS.StartProcessOnPort(); // OS assigns available port

        try
        {
            // Act - Should find the current process (listener runs in this test process)
            await Asserting.That(System.Infrastructure.ServiceDiscovery)
                .FindsCurrentProcessOnPort(testPort, TimeSpan.FromSeconds(5));
        }
        finally
        {
            // Cleanup
            System.OS.StopProcessOnPort(testPort);
        }
    }

    [Fact]
    public async Task FindServiceProcessIdAsync_WhenNoServiceOnPort_ShouldReturnNull()
    {
        // Arrange - port 54321 should be unused
        const int unusedPort = 54321;

        // Act & Assert
        await Asserting.That(System.Infrastructure.ServiceDiscovery)
            .FindsNoProcessOnPort(unusedPort, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void FindServiceProcessIdAsync_WhenInvalidPort_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        const int invalidPort = 99999;

        // Act & Assert
        Asserting.That(System.Infrastructure.ServiceDiscovery)
            .ThrowsArgumentOutOfRangeForInvalidPort(invalidPort);
    }
}
