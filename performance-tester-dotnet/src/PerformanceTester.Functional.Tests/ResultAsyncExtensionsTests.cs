using PerformanceTester.Functional;

namespace PerformanceTester.Functional.Tests;

public sealed class ResultAsyncExtensionsTests
{
    [Fact]
    public void Ensure_WhenSuccessAndPredicateHolds_ReturnsSuccess()
    {
        var result = new Result<int, string>.Success(5);

        var ensured = result.Ensure(x => x > 0, _ => "must be positive");

        Assert.True(ensured.IsSuccess);
        Assert.Equal(5, ensured.SuccessValue);
    }

    [Fact]
    public void Ensure_WhenSuccessAndPredicateFails_ReturnsFailureWithError()
    {
        var result = new Result<int, string>.Success(-5);

        var ensured = result.Ensure(x => x > 0, x => $"{x} is not positive");

        Assert.True(ensured.IsFailure);
        Assert.Equal("-5 is not positive", ensured.FailureError);
    }

    [Fact]
    public void Ensure_WhenFailure_PassesThroughAndDoesNotCallPredicate()
    {
        var result = new Result<int, string>.Failure("boom");
        var predicateCalled = false;

        var ensured = result.Ensure(
            x =>
            {
                predicateCalled = true;
                return true;
            },
            _ => "err");

        Assert.True(ensured.IsFailure);
        Assert.Equal("boom", ensured.FailureError);
        Assert.False(predicateCalled);
    }

    [Fact]
    public async Task BindAsync_WhenSuccess_InvokesBind()
    {
        var result = new Result<int, string>.Success(3);

        var bound = await result.BindAsync(x =>
            Task.FromResult<Result<string, string>>(new Result<string, string>.Success($"v{x}")));

        Assert.True(bound.IsSuccess);
        Assert.Equal("v3", bound.SuccessValue);
    }

    [Fact]
    public async Task BindAsync_WhenFailure_ShortCircuits()
    {
        var result = new Result<int, string>.Failure("bad");
        var bindCalled = false;

        var bound = await result.BindAsync(x =>
        {
            bindCalled = true;
            return Task.FromResult<Result<string, string>>(new Result<string, string>.Success("ignored"));
        });

        Assert.True(bound.IsFailure);
        Assert.Equal("bad", bound.FailureError);
        Assert.False(bindCalled);
    }

    [Fact]
    public async Task Tap_WhenSuccess_RunsActionAndPassesValueThrough()
    {
        var captured = 0;
        var task = Task.FromResult<Result<int, string>>(new Result<int, string>.Success(7));

        var result = await task.Tap(x => captured = x);

        Assert.Equal(7, captured);
        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.SuccessValue);
    }

    [Fact]
    public async Task Tap_WhenFailure_DoesNotRunAction()
    {
        var actionRan = false;
        var task = Task.FromResult<Result<int, string>>(new Result<int, string>.Failure("e"));

        var result = await task.Tap(_ => actionRan = true);

        Assert.False(actionRan);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task TapError_WhenFailure_RunsActionWithError()
    {
        string? captured = null;
        var task = Task.FromResult<Result<int, string>>(new Result<int, string>.Failure("oops"));

        var result = await task.TapError(e => captured = e);

        Assert.Equal("oops", captured);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task TapError_WhenSuccess_DoesNotRunAction()
    {
        var actionRan = false;
        var task = Task.FromResult<Result<int, string>>(new Result<int, string>.Success(1));

        var result = await task.TapError(_ => actionRan = true);

        Assert.False(actionRan);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task MapError_WhenFailure_TransformsError()
    {
        var task = Task.FromResult<Result<int, string>>(new Result<int, string>.Failure("low"));

        var result = await task.MapError(e => $"mapped:{e}");

        Assert.True(result.IsFailure);
        Assert.Equal("mapped:low", result.FailureError);
    }

    [Fact]
    public async Task MapError_WhenSuccess_PassesThroughUnchanged()
    {
        var task = Task.FromResult<Result<int, string>>(new Result<int, string>.Success(42));

        var result = await task.MapError(e => $"mapped:{e}");

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.SuccessValue);
    }
}
