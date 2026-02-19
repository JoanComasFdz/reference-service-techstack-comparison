using PerformanceTester.DockerMonitoring;
using PerformanceTester.DockerMonitoring.Monitoring;
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
    /// Asserts that a specific phase was received for a container.
    /// </summary>
    public static void AssertPhaseReceived(
        this DockerMonitorPhaseAwaiter awaiter,
        string containerName,
        DockerMonitorPhase phase)
    {
        var found = awaiter.ReceivedPhases.Any(p =>
            p.ContainerName == containerName &&
            p.Phase == phase);

        if (!found)
        {
            var actualPhases = string.Join(", ", awaiter.ReceivedPhases
                .Where(p => p.ContainerName == containerName)
                .Select(p => $"{p.Phase}/{p.State}"));

            throw new Xunit.Sdk.XunitException(
                $"Expected phase '{phase}' for container '{containerName}' was not received. " +
                $"Actual phases: [{actualPhases}]");
        }
    }

    /// <summary>
    /// Asserts that a specific phase with a specific state was received for a container.
    /// </summary>
    public static void AssertPhaseReceived(
        this DockerMonitorPhaseAwaiter awaiter,
        string containerName,
        DockerMonitorPhase phase,
        DockerMonitorPhaseState state)
    {
        var found = awaiter.ReceivedPhases.Any(p =>
            p.ContainerName == containerName &&
            p.Phase == phase &&
            p.State == state);

        if (!found)
        {
            var actualPhases = string.Join(", ", awaiter.ReceivedPhases
                .Where(p => p.ContainerName == containerName)
                .Select(p => $"{p.Phase}/{p.State}"));

            throw new Xunit.Sdk.XunitException(
                $"Expected phase '{phase}/{state}' for container '{containerName}' was not received. " +
                $"Actual phases: [{actualPhases}]");
        }
    }
}
