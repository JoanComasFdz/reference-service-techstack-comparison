using JoanComasFdz.AssertingThat;
using Xunit;

namespace PerformanceTester.EventPublishing.IntegrationTests.Infrastructure;

/// <summary>
/// Custom assertion extensions for EventPublishing integration tests.
/// Makes tests more readable by expressing assertions in domain language.
/// Uses AssertingThat pattern for fluent chaining with xUnit Assert methods internally.
/// </summary>
public static class EventPublishingAssertions
{
    /// <summary>
    /// Asserts that publish metrics indicate successful publishing.
    /// </summary>
    public static AssertingThat<PublishMetrics> HasPublishedSuccessfully(
        this AssertingThat<PublishMetrics> assertingThat,
        int expectedCount)
    {
        Assert.NotNull(assertingThat.InstanceToAssert);
        Assert.Equal(expectedCount, assertingThat.InstanceToAssert.EventCount);
        Assert.True(assertingThat.InstanceToAssert.Duration.TotalMilliseconds > 0);
        Assert.True(assertingThat.InstanceToAssert.EventsPerSecond > 0);
        return assertingThat;
    }

    /// <summary>
    /// Asserts that throughput meets minimum expected rate.
    /// </summary>
    public static AssertingThat<PublishMetrics> HasMinimumThroughput(
        this AssertingThat<PublishMetrics> assertingThat,
        double minimumEventsPerSecond)
    {
        Assert.True(
            assertingThat.InstanceToAssert.EventsPerSecond >= minimumEventsPerSecond,
            $"Expected throughput >= {minimumEventsPerSecond} events/sec, but was {assertingThat.InstanceToAssert.EventsPerSecond:F2}");
        return assertingThat;
    }

    /// <summary>
    /// Asserts that calling PublishEvents with invalid count throws ArgumentOutOfRangeException.
    /// </summary>
    public static AssertingThat<PublishEventsDelegate> ThrowsArgumentOutOfRangeForInvalidCount(
        this AssertingThat<PublishEventsDelegate> assertingThat,
        int invalidCount)
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await assertingThat.InstanceToAssert(invalidCount))
            .Wait();
        return assertingThat;
    }
}
