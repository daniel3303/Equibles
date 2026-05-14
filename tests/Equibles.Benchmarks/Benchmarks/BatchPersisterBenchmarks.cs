using BenchmarkDotNet.Attributes;
using Equibles.Worker;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// Per-import cost of <see cref="BatchPersister.Persist"/>'s batching machinery, isolated from
/// the flush delegate. Every scraper (holdings, congress, FRED, etc.) streams its rows through
/// this method in batches of 500–5 000 — the iteration, list churn, and clear-between-batches
/// add up across millions of rows per cycle. The flush callback is a no-op so the benchmark
/// captures only the buffer overhead, not the EF Core insert cost on the other side.
/// </summary>
[MemoryDiagnoser]
public class BatchPersisterBenchmarks
{
    private const int ItemCount = 10_000;
    private const int BatchSize = 500;
    private int[] _items;
    private static readonly Func<List<int>, Task> NoopFlush = _ => Task.CompletedTask;

    [GlobalSetup]
    public void Setup()
    {
        _items = new int[ItemCount];
        for (var i = 0; i < ItemCount; i++)
            _items[i] = i;
    }

    [Benchmark]
    public Task<int> StreamItemsThroughBatcher() =>
        BatchPersister.Persist(_items, BatchSize, NoopFlush);
}
