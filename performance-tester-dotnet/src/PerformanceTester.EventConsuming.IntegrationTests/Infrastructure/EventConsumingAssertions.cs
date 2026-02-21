using JoanComasFdz.AssertingThat;
using PerformanceTester.Infrastructure.ValueObjects;
using Xunit;

namespace PerformanceTester.EventConsuming.IntegrationTests.Infrastructure;

/// <summary>
/// Custom assertion extensions for EventConsuming integration tests.
/// Makes tests more readable by expressing assertions in domain language.
/// Uses AssertingThat pattern for fluent chaining with xUnit Assert methods internally.
/// </summary>
public static class EventConsumingAssertions
{
    /// <summary>
    /// Asserts that throughput samples were collected.
    /// </summary>
    public static AssertingThat<IMetricsCollector> HasThroughputSamples(
        this AssertingThat<IMetricsCollector> assertingThat)
    {
        var samples = assertingThat.InstanceToAssert.GetThroughputSamples();
        Assert.NotNull(samples);
        Assert.NotEmpty(samples);
        return assertingThat;
    }

    /// <summary>
    /// Asserts that the number of samples is at least the expected minimum.
    /// </summary>
    public static AssertingThat<IMetricsCollector> HasAtLeastThroughputSamples(
        this AssertingThat<IMetricsCollector> assertingThat,
        int minimumCount)
    {
        var samples = assertingThat.InstanceToAssert.GetThroughputSamples();
        Assert.True(
            samples.Count >= minimumCount,
            $"Expected at least {minimumCount} samples, but found {samples.Count}");
        return assertingThat;
    }

    /// <summary>
    /// Asserts that all throughput samples have valid data.
    /// </summary>
    public static AssertingThat<IMetricsCollector> HasValidThroughputData(
        this AssertingThat<IMetricsCollector> assertingThat)
    {
        var samples = assertingThat.InstanceToAssert.GetThroughputSamples();
        foreach (var sample in samples)
        {
            Assert.True(sample.ThroughputEventsPerSecond >= 0,
                $"Throughput cannot be negative: {sample.ThroughputEventsPerSecond}");
            Assert.True(sample.CumulativeEventCount > 0,
                $"Cumulative count must be positive: {sample.CumulativeEventCount}");
        }
        return assertingThat;
    }

    /// <summary>
    /// Asserts that StartTrackingEventsAsync throws TimeoutException with expected details.
    /// </summary>
    public static async Task<AssertingThat<IEventConsumer>> ThrowsTimeoutExceptionAfterStartTrackingEvents(
        this AssertingThat<IEventConsumer> assertingThat,
        EventCount expectedCount,
        TimeSpan inactivityTimeout,
        int expectedReceivedCount)
    {
        // Encapsulate the complex assertion logic
        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await assertingThat.InstanceToAssert.StartTrackingEventsAsync(
                expectedCount: expectedCount,
                inactivityTimeout: inactivityTimeout);
        });

        Assert.Contains("Inactivity timeout expired", exception.Message);
        Assert.Contains($"{expectedReceivedCount}/{expectedCount}", exception.Message);
        return assertingThat;
    }

    /// <summary>
    /// Asserts that StartTrackingEventsAsync throws ArgumentOutOfRangeException for invalid timeout.
    /// </summary>
    public static async Task<AssertingThat<IEventConsumer>> ThrowsArgumentOutOfRangeExceptionForInvalidTimeout(
        this AssertingThat<IEventConsumer> assertingThat,
        TimeSpan invalidTimeout)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await assertingThat.InstanceToAssert.StartTrackingEventsAsync(
                expectedCount: EventCount.Create(10).SuccessValue,
                inactivityTimeout: invalidTimeout);
        });
        return assertingThat;
    }
}
