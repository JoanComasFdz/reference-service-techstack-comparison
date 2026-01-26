using PerformanceTester.DockerMonitoring;
using Xunit;

namespace PerformanceTester.DockerMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Extension methods for DockerMonitorPhaseAwaiter to enable fluent test assertions.
/// Aligns with ConsumerPhaseAwaiterExtensions pattern from EventConsuming slice.
/// </summary>
public static class DockerMonitorPhaseAwaiterExtensions
{
    /// <summary>
    /// Asserts that the specified phases were received in order for any container.
    /// Phases can be interleaved between containers but must appear in order per container.
    /// </summary>
    public static void AssertPhasesReceivedInOrder(
        this DockerMonitorPhaseAwaiter awaiter,
        params (DockerMonitorPhase Phase, DockerMonitorPhaseState State)[] expectedPhases)
    {
        var received = awaiter.ReceivedPhases;

        int receivedIndex = 0;
        foreach (var (Phase, State) in expectedPhases)
        {
            bool found = false;
            while (receivedIndex < received.Count)
            {
                var current = received[receivedIndex];
                receivedIndex++;

                if (current.Phase == Phase && current.State == State)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Expected phase {Phase}/{State} was not found in order. " +
                    $"Received phases: [{string.Join(", ", received.Select(p => $"{p.ContainerName}:{p.Phase}/{p.State}"))}]");
            }
        }
    }

    /// <summary>
    /// Asserts that the specified phases were received in order for a specific container.
    /// </summary>
    public static void AssertPhasesReceivedInOrderForContainer(
        this DockerMonitorPhaseAwaiter awaiter,
        string containerName,
        params (DockerMonitorPhase Phase, DockerMonitorPhaseState State)[] expectedPhases)
    {
        var received = awaiter.ReceivedPhases
            .Where(p => p.ContainerName == containerName)
            .ToList();

        int receivedIndex = 0;
        foreach (var (Phase, State) in expectedPhases)
        {
            bool found = false;
            while (receivedIndex < received.Count)
            {
                var current = received[receivedIndex];
                receivedIndex++;

                if (current.Phase == Phase && current.State == State)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Expected phase {Phase}/{State} was not found in order for container {containerName}. " +
                    $"Received phases: [{string.Join(", ", received.Select(p => $"{p.Phase}/{p.State}"))}]");
            }
        }
    }

    /// <summary>
    /// Asserts that at least the specified number of samples were collected for a container.
    /// </summary>
    public static void AssertSampleCountAtLeast(
        this DockerMonitorPhaseAwaiter awaiter,
        string containerName,
        int minimumCount)
    {
        var currentCount = awaiter.GetSampleCount(containerName);
        Assert.True(
            currentCount >= minimumCount,
            $"Expected at least {minimumCount} samples for {containerName}, but got {currentCount}");
    }

    /// <summary>
    /// Asserts that the ContainerNotFound phase was received for a specific container.
    /// </summary>
    public static void AssertContainerNotFoundReceived(
        this DockerMonitorPhaseAwaiter awaiter,
        string containerName)
    {
        var received = awaiter.ReceivedPhases;
        var found = received.Any(p =>
            p.ContainerName == containerName &&
            p.Phase == DockerMonitorPhase.ContainerNotFound);

        Assert.True(found,
            $"Expected ContainerNotFound phase for {containerName}, but it was not received. " +
            $"Received phases: [{string.Join(", ", received.Select(p => $"{p.ContainerName}:{p.Phase}/{p.State}"))}]");
    }
}
