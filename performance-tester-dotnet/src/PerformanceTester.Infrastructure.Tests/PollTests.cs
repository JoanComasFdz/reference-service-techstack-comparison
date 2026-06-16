using PerformanceTester.Functional;
using PerformanceTester.Infrastructure.ProcessFinding;

namespace PerformanceTester.Infrastructure.Tests;

public sealed class PollTests
{
    [Fact]
    public async Task UntilSuccessOrTimeoutAsync_WhenAttemptSucceedsFirstTime_ReturnsSuccessAfterOneAttempt()
    {
        var attempts = 0;

        var result = await Poll.UntilSuccessOrTimeoutAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult<Result<int, string>>(new Result<int, string>.Success(10));
            },
            interval: TimeSpan.FromMilliseconds(1),
            timeout: TimeSpan.FromMilliseconds(5),
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.SuccessValue);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task UntilSuccessOrTimeoutAsync_WhenAttemptSucceedsOnThirdTry_StopsAtThatAttempt()
    {
        var attempts = 0;

        var result = await Poll.UntilSuccessOrTimeoutAsync(
            _ =>
            {
                attempts++;
                Result<int, string> r = attempts >= 3
                    ? new Result<int, string>.Success(attempts)
                    : new Result<int, string>.Failure($"fail {attempts}");
                return Task.FromResult(r);
            },
            interval: TimeSpan.FromMilliseconds(1),
            timeout: TimeSpan.FromMilliseconds(20),
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.SuccessValue);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task UntilSuccessOrTimeoutAsync_WhenAllAttemptsFail_RunsMaxAttemptsAndReturnsLastFailure()
    {
        var attempts = 0;

        var result = await Poll.UntilSuccessOrTimeoutAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult<Result<int, string>>(
                    new Result<int, string>.Failure($"fail {attempts}"));
            },
            interval: TimeSpan.FromMilliseconds(1),
            timeout: TimeSpan.FromMilliseconds(5),
            cancellationToken: CancellationToken.None);

        // maxAttempts = ceil(5 / 1) = 5
        Assert.True(result.IsFailure);
        Assert.Equal("fail 5", result.FailureError);
        Assert.Equal(5, attempts);
    }

    [Fact]
    public async Task UntilSuccessOrTimeoutAsync_WhenTimeoutSmallerThanInterval_RunsAtLeastOneAttempt()
    {
        var attempts = 0;

        var result = await Poll.UntilSuccessOrTimeoutAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult<Result<int, string>>(new Result<int, string>.Failure("nope"));
            },
            interval: TimeSpan.FromSeconds(1),
            timeout: TimeSpan.Zero,
            cancellationToken: CancellationToken.None);

        // maxAttempts = max(1, ceil(0 / 1)) = 1
        Assert.True(result.IsFailure);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task UntilSuccessOrTimeoutAsync_AttemptCountIsIndependentOfAttemptDuration()
    {
        var attempts = 0;

        var result = await Poll.UntilSuccessOrTimeoutAsync(
            async ct =>
            {
                attempts++;
                await Task.Delay(TimeSpan.FromMilliseconds(20), ct); // each attempt is slower than the interval
                Result<int, string> r = new Result<int, string>.Failure($"fail {attempts}");
                return r;
            },
            interval: TimeSpan.FromMilliseconds(1),
            timeout: TimeSpan.FromMilliseconds(5),
            cancellationToken: CancellationToken.None);

        // maxAttempts = ceil(5 / 1) = 5, regardless of each attempt taking ~20ms.
        // (The current wall-clock Poll stops after attempt 1 here — this is the deterministic red test.)
        Assert.True(result.IsFailure);
        Assert.Equal("fail 5", result.FailureError);
        Assert.Equal(5, attempts);
    }

    [Fact]
    public async Task UntilSuccessOrTimeoutAsync_WhenTokenAlreadyCancelled_ThrowsWithoutAttempting()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var attempts = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Poll.UntilSuccessOrTimeoutAsync(
                _ =>
                {
                    attempts++;
                    return Task.FromResult<Result<int, string>>(new Result<int, string>.Failure("x"));
                },
                interval: TimeSpan.FromMilliseconds(1),
                timeout: TimeSpan.FromMilliseconds(5),
                cancellationToken: cts.Token));

        Assert.Equal(0, attempts);
    }
}
