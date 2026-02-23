using Xunit;

namespace PerformanceTester.ProcessMonitoring.IntegrationTests.Infrastructure;

/// <summary>
/// Extension methods for ProcessMonitorPhaseAwaiter to enable fluent test assertions.
/// Aligns with DockerMonitorPhaseAwaiterExtensions pattern.
/// </summary>
public static class ProcessMonitorPhaseAwaiterExtensions
{
    /// <summary>
    /// Asserts that the specified phases were received in order.
    /// </summary>
    public static void AssertPhasesReceivedInOrder(
        this ProcessMonitorPhaseAwaiter awaiter,
        params (ProcessMonitorPhase Phase, ProcessMonitorPhaseState State)[] expectedPhases)
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
                    $"Received phases: [{string.Join(", ", received.Select(p => $"{p.Phase}/{p.State}"))}]");
            }
        }
    }

    /// <summary>
    /// Asserts that at least the specified number of samples were collected.
    /// </summary>
    public static void AssertSampleCountAtLeast(
        this ProcessMonitorPhaseAwaiter awaiter,
        int minimumCount)
    {
        var currentCount = awaiter.SampleCount;
        Assert.True(
            currentCount >= minimumCount,
            $"Expected at least {minimumCount} samples, but got {currentCount}");
    }

    /// <summary>
    /// Asserts that the ProcessNotFound phase was received.
    /// </summary>
    public static void AssertProcessNotFoundReceived(this ProcessMonitorPhaseAwaiter awaiter)
    {
        var received = awaiter.ReceivedPhases;
        var found = received.Any(p => p.Phase == ProcessMonitorPhase.ProcessNotFound);

        Assert.True(found,
            $"Expected ProcessNotFound phase, but it was not received. " +
            $"Received phases: [{string.Join(", ", received.Select(p => $"{p.Phase}/{p.State}"))}]");
    }

    /// <summary>
    /// Asserts that the ProcessExited phase was received.
    /// </summary>
    public static void AssertProcessExitedReceived(this ProcessMonitorPhaseAwaiter awaiter)
    {
        var received = awaiter.ReceivedPhases;
        var found = received.Any(p => p.Phase == ProcessMonitorPhase.ProcessExited);

        Assert.True(found,
            $"Expected ProcessExited phase, but it was not received. " +
            $"Received phases: [{string.Join(", ", received.Select(p => $"{p.Phase}/{p.State}"))}]");
    }

    /// <summary>
    /// Asserts that the FirstSampleCollected phase was received.
    /// </summary>
    public static void AssertFirstSampleCollectedReceived(this ProcessMonitorPhaseAwaiter awaiter)
    {
        var received = awaiter.ReceivedPhases;
        var found = received.Any(p => p.Phase == ProcessMonitorPhase.FirstSampleCollected);

        Assert.True(found,
            $"Expected FirstSampleCollected phase, but it was not received. " +
            $"Received phases: [{string.Join(", ", received.Select(p => $"{p.Phase}/{p.State}"))}]");
    }
}
