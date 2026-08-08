using Equibles.Mcp.Extensions;
using Microsoft.Extensions.Caching.Memory;

namespace Equibles.UnitTests.Mcp;

public class MemoryCacheExtensionsGetOrCreateSafeAsyncTests
{
    // Contract: a get-or-create — the factory builds the value on a miss, the
    // result is cached, and a later call for the same key is served from the
    // cache without re-running the factory. Two sequential calls must run the
    // (expensive) factory exactly once.
    [Fact]
    public async Task GetOrCreateSafeAsync_SecondCallForSameKey_ServesCachedAndDoesNotReinvokeFactory()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var key = $"served-once-{Guid.NewGuid()}";
        var factoryCalls = 0;
        Task<int> Factory()
        {
            factoryCalls++;
            return Task.FromResult(42);
        }

        var first = await cache.GetOrCreateSafeAsync(key, TimeSpan.FromMinutes(5), Factory);
        var second = await cache.GetOrCreateSafeAsync(key, TimeSpan.FromMinutes(5), Factory);

        first.Should().Be(42);
        second.Should().Be(42);
        factoryCalls.Should().Be(1);
    }

    // Contract: the "Safe" is stampede protection — concurrent callers for the
    // same uncached key run the factory exactly once; the rest queue on the
    // per-key lock and pick up the first caller's cached result. Without this a
    // cold cache under load runs N identical whole-universe computations.
    [Fact]
    public async Task GetOrCreateSafeAsync_ConcurrentCallsSameKey_ExecutesFactoryExactlyOnce()
    {
        var factoryEntered = new SemaphoreSlim(0, 1);
        var factoryRelease = new TaskCompletionSource<int>();
        var callCount = 0;
        var cache = new MemoryCache(new MemoryCacheOptions());
        var key = $"stampede-{Guid.NewGuid()}";

        var task1 = cache.GetOrCreateSafeAsync(
            key,
            TimeSpan.FromMinutes(5),
            async () =>
            {
                Interlocked.Increment(ref callCount);
                factoryEntered.Release();
                return await factoryRelease.Task;
            }
        );

        await factoryEntered.WaitAsync();

        var task2 = cache.GetOrCreateSafeAsync(
            key,
            TimeSpan.FromMinutes(5),
            async () =>
            {
                Interlocked.Increment(ref callCount);
                return 99;
            }
        );

        factoryRelease.SetResult(42);

        var result1 = await task1;
        var result2 = await task2;

        result1.Should().Be(42);
        result2.Should().Be(42);
        callCount.Should().Be(1);
    }

    // A throwing factory must propagate but release the per-key semaphore in the
    // finally — otherwise every later caller of that key deadlocks. And nothing
    // is cached, so a later call re-runs the factory and can succeed.
    [Fact]
    public async Task GetOrCreateSafeAsync_FactoryThrows_ReleasesLockAndCachesNothing()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var key = $"throws-{Guid.NewGuid()}";

        var first = async () =>
            await cache.GetOrCreateSafeAsync<int>(
                key,
                TimeSpan.FromMinutes(5),
                () => throw new InvalidOperationException("boom")
            );
        await first.Should().ThrowAsync<InvalidOperationException>();

        var second = await cache.GetOrCreateSafeAsync(
            key,
            TimeSpan.FromMinutes(5),
            () => Task.FromResult(42)
        );
        second.Should().Be(42);
    }

    // A queued caller may abandon its wait with its own request's token — it
    // throws OperationCanceledException — but cancelling the WAIT never aborts
    // the in-flight factory, which completes and caches for everyone else.
    [Fact]
    public async Task GetOrCreateSafeAsync_WaiterCancelled_FactoryStillCompletesAndCaches()
    {
        var factoryEntered = new SemaphoreSlim(0, 1);
        var factoryRelease = new TaskCompletionSource<int>();
        var factoryCalls = 0;
        var cache = new MemoryCache(new MemoryCacheOptions());
        var key = $"waiter-cancelled-{Guid.NewGuid()}";

        var task1 = cache.GetOrCreateSafeAsync(
            key,
            TimeSpan.FromMinutes(5),
            async () =>
            {
                Interlocked.Increment(ref factoryCalls);
                factoryEntered.Release();
                return await factoryRelease.Task;
            }
        );

        await factoryEntered.WaitAsync();

        using var waiterCancellation = new CancellationTokenSource();
        var task2 = cache.GetOrCreateSafeAsync(
            key,
            TimeSpan.FromMinutes(5),
            () => Task.FromResult(99),
            waiterCancellation.Token
        );
        waiterCancellation.Cancel();

        var abandoned = async () => await task2;
        await abandoned.Should().ThrowAsync<OperationCanceledException>();

        factoryRelease.SetResult(42);
        (await task1).Should().Be(42);

        var later = await cache.GetOrCreateSafeAsync(
            key,
            TimeSpan.FromMinutes(5),
            () => Task.FromResult(7)
        );
        later.Should().Be(42);
        factoryCalls.Should().Be(1);
    }

    // A factory that legitimately produces null has that null cached and served:
    // TryGetValue reports the entry as present, so later calls do not re-run the
    // factory just because the value is null.
    [Fact]
    public async Task GetOrCreateSafeAsync_FactoryReturnsNull_CachesAndServesTheNull()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var key = $"null-{Guid.NewGuid()}";
        var factoryCalls = 0;
        Task<string> Factory()
        {
            factoryCalls++;
            return Task.FromResult<string>(null);
        }

        var first = await cache.GetOrCreateSafeAsync(key, TimeSpan.FromMinutes(5), Factory);
        var second = await cache.GetOrCreateSafeAsync(key, TimeSpan.FromMinutes(5), Factory);

        first.Should().BeNull();
        second.Should().BeNull();
        factoryCalls.Should().Be(1);
    }
}
