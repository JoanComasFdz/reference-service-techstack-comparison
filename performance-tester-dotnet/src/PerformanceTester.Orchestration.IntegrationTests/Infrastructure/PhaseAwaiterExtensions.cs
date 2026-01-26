using PerformanceTester.Orchestration;
using Xunit;

namespace PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

/// <summary>
/// Extension methods for PhaseAwaiter to enable fluent test syntax.
/// </summary>
public static class PhaseAwaiterExtensions
{
    /// <summary>
    /// Asserts that the specified phases were received in order.
    /// </summary>
    public static void AssertPhasesReceivedInOrder(
        this PhaseAwaiter awaiter,
        params (TestPhase Phase, PhaseState State)[] expectedPhases)
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
    /// Asserts that all standard phases were completed successfully.
    /// </summary>
    public static void AssertAllPhasesCompleted(this PhaseAwaiter awaiter)
    {
        awaiter.AssertPhasesReceivedInOrder(
            (TestPhase.Setup, PhaseState.Starting),
            (TestPhase.Setup, PhaseState.Completed),
            (TestPhase.Warmup, PhaseState.Starting),
            (TestPhase.Warmup, PhaseState.Completed),
            (TestPhase.EventTest, PhaseState.Starting),
            (TestPhase.EventTest, PhaseState.Completed),
            (TestPhase.ApiTest, PhaseState.Starting),
            (TestPhase.ApiTest, PhaseState.Completed),
            (TestPhase.Reporting, PhaseState.Starting),
            (TestPhase.Reporting, PhaseState.Completed));
    }
}
