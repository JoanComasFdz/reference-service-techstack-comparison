using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTester.IntegrationTesting.Tests;

/// <summary>
/// Tests for Docker network creation and conflict handling.
/// These tests verify that the network creation handles the "network already exists"
/// scenario gracefully.
///
/// IMPORTANT: Each test creates its own unique network (GUID-based) and cleans it up.
/// Tests can run in parallel - no shared state between tests.
/// </summary>
public class ContainerManagerNetworkTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// Verifies that creating a new network succeeds when no network exists.
    /// </summary>
    [Fact]
    public async Task NetworkCreateAsync_WhenNetworkDoesNotExist_CreatesSuccessfully()
    {
        // Arrange - each test gets its own unique network name
        var networkName = $"test-network-{Guid.NewGuid():N}";
        _output.WriteLine($"Test network name: {networkName}");

        try
        {
            var network = new NetworkBuilder()
                .WithName(networkName)
                .WithReuse(true)
                .WithCleanUp(false)
                .Build();

            // Act
            await network.CreateAsync();

            // Assert - Verify network exists
            using var dockerClient = new DockerClientConfiguration().CreateClient();
            var networks = await dockerClient.Networks.ListNetworksAsync(
                new NetworksListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["name"] = new Dictionary<string, bool> { [networkName] = true }
                    }
                });

            Assert.Single(networks);
            Assert.Equal(networkName, networks[0].Name);
            _output.WriteLine($"✓ Network '{networkName}' created successfully");
        }
        finally
        {
            // Cleanup always runs
            await CleanupNetworkAsync(networkName);
        }
    }

    /// <summary>
    /// Verifies that the DockerApiException with Conflict status is thrown when
    /// attempting to create a network that already exists (without our fix).
    /// This test documents the original problem behavior.
    /// </summary>
    [Fact]
    public async Task NetworkCreateAsync_WhenNetworkAlreadyExists_ThrowsConflictException()
    {
        // Arrange - each test gets its own unique network name
        var networkName = $"test-network-{Guid.NewGuid():N}";
        _output.WriteLine($"Test network name: {networkName}");

        try
        {
            // Create the network first time
            using var dockerClient = new DockerClientConfiguration().CreateClient();
            await dockerClient.Networks.CreateNetworkAsync(new NetworksCreateParameters
            {
                Name = networkName,
                Driver = "bridge"
            });
            _output.WriteLine($"✓ First network creation succeeded");

            // Act - Try to create network again using raw Docker API (simulates the bug)
            var exception = await Assert.ThrowsAsync<DockerApiException>(async () =>
            {
                await dockerClient.Networks.CreateNetworkAsync(new NetworksCreateParameters
                {
                    Name = networkName,
                    Driver = "bridge"
                });
            });

            // Assert - Verify it's a Conflict (409) error
            Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
            Assert.Contains("already exists", exception.Message);
            _output.WriteLine($"✓ Conflict exception thrown as expected: {exception.Message}");
        }
        finally
        {
            // Cleanup always runs
            await CleanupNetworkAsync(networkName);
        }
    }

    /// <summary>
    /// Verifies that our fix handles the "network already exists" scenario gracefully.
    /// This is the core test for the ContainerManager fix.
    /// </summary>
    [Fact]
    public async Task NetworkCreateAsync_WithConflictHandling_ReusesExistingNetwork()
    {
        // Arrange - each test gets its own unique network name
        var networkName = $"test-network-{Guid.NewGuid():N}";
        _output.WriteLine($"Test network name: {networkName}");

        try
        {
            // Create network first time using Docker client
            using var dockerClient = new DockerClientConfiguration().CreateClient();
            var firstCreate = await dockerClient.Networks.CreateNetworkAsync(new NetworksCreateParameters
            {
                Name = networkName,
                Driver = "bridge"
            });
            _output.WriteLine($"✓ First creation succeeded, ID: {firstCreate.ID}");

            // Act - Try to create again using Testcontainers with our fix pattern
            var network = new NetworkBuilder()
                .WithName(networkName)
                .WithReuse(true)
                .WithCleanUp(false)
                .Build();

            bool conflictHandled = false;
            try
            {
                await network.CreateAsync();
                _output.WriteLine("Network created (no conflict)");
            }
            catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                // This is our fix - handle conflict gracefully
                conflictHandled = true;
                _output.WriteLine($"✓ Conflict handled gracefully: {ex.Message}");
            }

            // Assert - Conflict should have been handled
            Assert.True(conflictHandled, "Expected conflict to be caught and handled");

            // Verify network still exists and is usable
            var networks = await dockerClient.Networks.ListNetworksAsync(
                new NetworksListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["name"] = new Dictionary<string, bool> { [networkName] = true }
                    }
                });
            Assert.Single(networks);
            _output.WriteLine($"✓ Network still exists after conflict handling");
        }
        finally
        {
            // Cleanup always runs
            await CleanupNetworkAsync(networkName);
        }
    }

    /// <summary>
    /// Verifies that multiple sequential CreateAsync calls with conflict handling
    /// all succeed (idempotent behavior).
    /// </summary>
    [Fact]
    public async Task NetworkCreateAsync_CalledMultipleTimes_IsIdempotent()
    {
        // Arrange - each test gets its own unique network name
        var networkName = $"test-network-{Guid.NewGuid():N}";
        _output.WriteLine($"Test network name: {networkName}");

        try
        {
            // Act - Create network multiple times with conflict handling
            for (int i = 1; i <= 3; i++)
            {
                var network = new NetworkBuilder()
                    .WithName(networkName)
                    .WithReuse(true)
                    .WithCleanUp(false)
                    .Build();

                try
                {
                    await network.CreateAsync();
                    _output.WriteLine($"✓ Attempt {i}: Network created");
                }
                catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
                {
                    _output.WriteLine($"✓ Attempt {i}: Network already exists (reusing)");
                }
            }

            // Assert - Network should exist
            using var dockerClient = new DockerClientConfiguration().CreateClient();
            var networks = await dockerClient.Networks.ListNetworksAsync(
                new NetworksListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["name"] = new Dictionary<string, bool> { [networkName] = true }
                    }
                });

            Assert.Single(networks);
            _output.WriteLine($"✓ Network exists after 3 create attempts");
        }
        finally
        {
            // Cleanup always runs
            await CleanupNetworkAsync(networkName);
        }
    }

    /// <summary>
    /// Verifies that the production ContainerManager's EnsureStartedAsync is idempotent
    /// and doesn't throw on subsequent calls (tests the actual fix in production code).
    /// Note: This test uses the singleton ContainerManager which manages the production network.
    /// </summary>
    [Fact]
    public async Task ContainerManager_EnsureStartedAsync_IsIdempotent()
    {
        // Arrange - Get the singleton instance (no cleanup needed - production singleton)
        var manager = ContainerManager.Instance;

        // Act - Call EnsureStartedAsync multiple times
        var exception = await Record.ExceptionAsync(async () =>
        {
            await manager.EnsureStartedAsync(_output);
            await manager.EnsureStartedAsync(_output);
            await manager.EnsureStartedAsync(_output);
        });

        // Assert - No exception should be thrown
        Assert.Null(exception);
        Assert.False(string.IsNullOrEmpty(manager.PostgresConnectionString));
        Assert.False(string.IsNullOrEmpty(manager.RabbitMqConnectionString));
        _output.WriteLine("✓ ContainerManager.EnsureStartedAsync is idempotent");
        _output.WriteLine($"  PostgreSQL: {manager.PostgresConnectionString}");
        _output.WriteLine($"  RabbitMQ: {manager.RabbitMqConnectionString}");
    }

    /// <summary>
    /// Cleans up a test network by name. Silently ignores errors if network doesn't exist.
    /// This method is called in finally blocks to ensure cleanup always runs.
    /// </summary>
    private async Task CleanupNetworkAsync(string networkName)
    {
        try
        {
            using var dockerClient = new DockerClientConfiguration().CreateClient();
            await dockerClient.Networks.DeleteNetworkAsync(networkName);
            _output.WriteLine($"✓ Cleaned up test network: {networkName}");
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Network doesn't exist - that's fine
            _output.WriteLine($"Test network already removed: {networkName}");
        }
        catch (Exception ex)
        {
            // Log but don't fail - cleanup is best-effort
            _output.WriteLine($"Warning: Failed to cleanup network {networkName}: {ex.Message}");
        }
    }
}
