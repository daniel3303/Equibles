using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Equibles.Mcp.Extensions;

public static class MemoryCacheExtensions
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    // Single-flight get-or-create for tools that cache an expensive whole-universe
    // computation: concurrent cold callers for the same key queue on a per-key
    // semaphore behind the one running the factory and reuse its result instead of
    // each paying the computation. The wait is cancellable so a queued caller can
    // abandon the wait with its own request; cancelling never aborts the in-flight
    // factory, which keeps running for whoever owns the lock.
    //
    // Keys must come from a bounded, program-controlled set (compile-time
    // constants) — never per-request input: lock entries are kept for the process
    // lifetime, so an unbounded key space grows the table forever.
    public static async Task<T> GetOrCreateSafeAsync<T>(
        this IMemoryCache cache,
        string key,
        TimeSpan duration,
        Func<Task<T>> factory,
        CancellationToken cancellationToken = default
    )
    {
        if (cache.TryGetValue(key, out T cached))
        {
            return cached;
        }

        var semaphore = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(key, out cached))
            {
                return cached;
            }

            var result = await factory();
            cache.Set(key, result, duration);
            return result;
        }
        finally
        {
            semaphore.Release();
        }
    }
}
