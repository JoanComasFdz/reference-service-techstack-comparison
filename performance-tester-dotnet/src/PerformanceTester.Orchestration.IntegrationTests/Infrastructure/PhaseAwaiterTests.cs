using PerformanceTester.Orchestration;
using Xunit;

namespace PerformanceTester.Orchestration.IntegrationTests.Infrastructure;

public class PhaseAwaiterTests
{
    [Fact]
    public async Task PhaseAwaiter_WhenPhaseReported_ShouldCompleteWaiter()
    {
        // Arrange
        var awaiter = new PhaseAwaiter();
        var waitTask = awaiter.WaitForPhaseStartAsync(TestPhase.ApiTest);

        // Act
        awaiter.Report(PhaseInfo.Starting(TestPhase.Setup));
        awaiter.Report(PhaseInfo.Completed(TestPhase.Setup));
        awaiter.Report(PhaseInfo.Starting(TestPhase.ApiTest));

        // Assert
        await waitTask; // Should complete without timeout
        Assert.Equal(3, awaiter.ReceivedPhases.Count);
    }

    [Fact]
    public async Task PhaseAwaiter_WhenPhaseAlreadyReceived_ShouldReturnImmediately()
    {
        // Arrange
        var awaiter = new PhaseAwaiter();
        awaiter.Report(PhaseInfo.Starting(TestPhase.ApiTest));

        // Act & Assert - Should return immediately, not wait
        await awaiter.WaitForPhaseStartAsync(TestPhase.ApiTest, timeout: TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task PhaseAwaiter_WhenTimeout_ShouldThrowTimeoutException()
    {
        // Arrange
        var awaiter = new PhaseAwaiter();

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await awaiter.WaitForPhaseStartAsync(TestPhase.ApiTest, timeout: TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void PhaseAwaiter_AssertPhasesReceivedInOrder_WhenPhasesInOrder_ShouldPass()
    {
        // Arrange
        var awaiter = new PhaseAwaiter();
        awaiter.Report(PhaseInfo.Starting(TestPhase.Setup));
        awaiter.Report(PhaseInfo.Completed(TestPhase.Setup));
        awaiter.Report(PhaseInfo.Starting(TestPhase.Warmup));
        awaiter.Report(PhaseInfo.Completed(TestPhase.Warmup));

        // Act & Assert - Should not throw
        awaiter.AssertPhasesReceivedInOrder(
            (TestPhase.Setup, PhaseState.Starting),
            (TestPhase.Setup, PhaseState.Completed),
            (TestPhase.Warmup, PhaseState.Starting),
            (TestPhase.Warmup, PhaseState.Completed));
    }

    [Fact]
    public void PhaseAwaiter_AssertPhasesReceivedInOrder_WhenPhaseMissing_ShouldThrow()
    {
        // Arrange
        var awaiter = new PhaseAwaiter();
        awaiter.Report(PhaseInfo.Starting(TestPhase.Setup));
        // Missing Setup Completed

        // Act & Assert
        var exception = Assert.Throws<Xunit.Sdk.XunitException>(() =>
            awaiter.AssertPhasesReceivedInOrder(
                (TestPhase.Setup, PhaseState.Starting),
                (TestPhase.Setup, PhaseState.Completed)));  // This should fail

        Assert.Contains("Setup/Completed", exception.Message);
    }

    [Fact]
    public async Task PhaseAwaiter_WhenWaitingForCompletion_ShouldCompleteOnCompletedState()
    {
        // Arrange
        var awaiter = new PhaseAwaiter();
        var waitTask = awaiter.WaitForPhaseCompleteAsync(TestPhase.EventTest);

        // Act
        awaiter.Report(PhaseInfo.Starting(TestPhase.EventTest));
        // Starting should not complete the wait
        Assert.False(waitTask.IsCompleted);

        awaiter.Report(PhaseInfo.Completed(TestPhase.EventTest));

        // Assert
        await waitTask; // Should complete now
        Assert.Equal(2, awaiter.ReceivedPhases.Count);
    }

    [Fact]
    public async Task PhaseAwaiter_WhenMultipleWaitersForSamePhase_AllShouldComplete()
    {
        // Arrange
        var awaiter = new PhaseAwaiter();
        var waitTask1 = awaiter.WaitForPhaseStartAsync(TestPhase.ApiTest);
        var waitTask2 = awaiter.WaitForPhaseStartAsync(TestPhase.ApiTest);

        // Act
        awaiter.Report(PhaseInfo.Starting(TestPhase.ApiTest));

        // Assert - Both should complete
        await Task.WhenAll(waitTask1, waitTask2);
    }
}
