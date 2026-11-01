using UrlShortener.Infrastructure.Services;
using Xunit;

namespace UrlShortener.UnitTests;

public class SingleFlightTests
{
    [Fact]
    public async Task ExecuteAsync_ConcurrentCallsSameKey_RunsActionOnce()
    {
        var singleFlight = new SingleFlight();
        int executionCount = 0;
        var startGate = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            await startGate.Task;
            return await singleFlight.ExecuteAsync("test-key", async () =>
            {
                Interlocked.Increment(ref executionCount);
                await Task.Delay(50);
                return "result-value";
            });
        }).ToList();

        startGate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, executionCount);
        Assert.All(results, r => Assert.Equal("result-value", r));
    }

    [Fact]
    public async Task ExecuteAsync_DifferentKeys_RunSeparately()
    {
        var singleFlight = new SingleFlight();
        int executionCount = 0;

        var task1 = singleFlight.ExecuteAsync("key-1", async () =>
        {
            Interlocked.Increment(ref executionCount);
            await Task.Delay(20);
            return 1;
        });

        var task2 = singleFlight.ExecuteAsync("key-2", async () =>
        {
            Interlocked.Increment(ref executionCount);
            await Task.Delay(20);
            return 2;
        });

        var results = await Task.WhenAll(task1, task2);

        Assert.Equal(2, executionCount);
        Assert.Equal(1, results[0]);
        Assert.Equal(2, results[1]);
    }

    [Fact]
    public async Task ExecuteAsync_WhenActionThrows_ExceptionPropagatedAndKeyFreed()
    {
        var singleFlight = new SingleFlight();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            singleFlight.ExecuteAsync<string>("error-key", () => throw new InvalidOperationException("boom")));

        // Subsequent call on same key succeeds after previous failure
        var result = await singleFlight.ExecuteAsync("error-key", () => Task.FromResult("recovered"));
        Assert.Equal("recovered", result);
    }
}
