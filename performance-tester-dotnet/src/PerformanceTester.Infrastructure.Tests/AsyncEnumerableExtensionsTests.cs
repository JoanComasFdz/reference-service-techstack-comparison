using PerformanceTester.Functional;

namespace PerformanceTester.Infrastructure.Tests;

public sealed class AsyncEnumerableExtensionsTests
{
    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }

    [Fact]
    public async Task Take_YieldsAtMostCountElements()
    {
        var taken = new List<int>();
        await foreach (var x in ToAsync(new[] { 1, 2, 3, 4, 5 }).Take(3))
        {
            taken.Add(x);
        }

        Assert.Equal(new[] { 1, 2, 3 }, taken);
    }

    [Fact]
    public async Task Take_WhenCountIsZero_YieldsNothing()
    {
        var taken = new List<int>();
        await foreach (var x in ToAsync(new[] { 1, 2, 3 }).Take(0))
        {
            taken.Add(x);
        }

        Assert.Empty(taken);
    }

    [Fact]
    public async Task SelectAwait_ProjectsEachElementInOrder()
    {
        var projected = new List<string>();
        await foreach (var s in ToAsync(new[] { 1, 2, 3 }).SelectAwait(x => Task.FromResult($"n{x}")))
        {
            projected.Add(s);
        }

        Assert.Equal(new[] { "n1", "n2", "n3" }, projected);
    }

    [Fact]
    public async Task FirstSuccessOrLastAsync_WhenSuccessExists_ReturnsFirstSuccessAndStops()
    {
        var pulled = 0;

        async IAsyncEnumerable<Result<int, string>> Source()
        {
            pulled++;
            yield return new Result<int, string>.Failure("f1");
            await Task.Yield();
            pulled++;
            yield return new Result<int, string>.Success(99);
            await Task.Yield();
            pulled++;
            yield return new Result<int, string>.Failure("f3");
        }

        var result = await Source().FirstSuccessOrLastAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(99, result.SuccessValue);
        Assert.Equal(2, pulled); // third element never produced (short-circuit on success)
    }

    [Fact]
    public async Task FirstSuccessOrLastAsync_WhenAllFail_ReturnsLastFailure()
    {
        var source = ToAsync(new Result<int, string>[]
        {
            new Result<int, string>.Failure("f1"),
            new Result<int, string>.Failure("f2"),
            new Result<int, string>.Failure("f3"),
        });

        var result = await source.FirstSuccessOrLastAsync();

        Assert.True(result.IsFailure);
        Assert.Equal("f3", result.FailureError);
    }

    [Fact]
    public async Task FirstSuccessOrLastAsync_WhenEmpty_Throws()
    {
        var empty = ToAsync(Array.Empty<Result<int, string>>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await empty.FirstSuccessOrLastAsync());
    }
}
