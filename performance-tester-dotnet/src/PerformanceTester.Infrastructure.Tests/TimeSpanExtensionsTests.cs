namespace PerformanceTester.Infrastructure.Tests;

public sealed class TimeSpanExtensionsTests
{
    [Fact]
    public async Task Tick_EmitsConsecutiveCountersFromZero()
    {
        var ticks = new List<int>();

        await foreach (var i in TimeSpan.FromMilliseconds(1).Tick())
        {
            ticks.Add(i);
            if (ticks.Count == 4)
            {
                break;
            }
        }

        Assert.Equal(new[] { 0, 1, 2, 3 }, ticks);
    }

    [Fact]
    public async Task Tick_WhenTokenAlreadyCancelled_ThrowsBeforeYielding()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var yielded = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in TimeSpan.FromMilliseconds(1).Tick(cts.Token))
            {
                yielded++;
            }
        });

        Assert.Equal(0, yielded);
    }
}
