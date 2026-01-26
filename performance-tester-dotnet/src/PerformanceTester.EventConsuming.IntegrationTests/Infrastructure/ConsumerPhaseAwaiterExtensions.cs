using PerformanceTester.EventConsuming;
using Xunit;

namespace PerformanceTester.EventConsuming.IntegrationTests.Infrastructure;

/// <summary>
/// Extension methods for ConsumerPhaseAwaiter to enable fluent test syntax.
/// </summary>
public static class ConsumerPhaseAwaiterExtensions
{
    /// <summary>
    /// Asserts that the specified phases were received in order.
    /// </summary>
    public static void AssertPhasesReceivedInOrder(
        this ConsumerPhaseAwaiter awaiter,
        params (ConsumerPhase Phase, ConsumerPhaseState State)[] expectedPhases)
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
    /// Asserts that at least the specified number of events were received.
    /// </summary>
    public static void AssertEventCountAtLeast(this ConsumerPhaseAwaiter awaiter, int minimumCount)
    {
        var currentCount = awaiter.CurrentEventCount;
        Assert.True(
            currentCount >= minimumCount,
            $"Expected at least {minimumCount} events, but received {currentCount}");
    }
}
