using BenchmarkDotNet.Attributes;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// Baseline benchmark for the holdings scraper's quarterly-period enumerator.
/// <see cref="HoldingsDataSetClient.GetDataSetFileNames"/> runs once per scraper cycle, and
/// the list it produces drives every SEC EDGAR 13F download. The function is pure and
/// allocation-shaped (one string per period, ~50 entries for a 2013-onward backfill), so
/// it's a clean baseline for tracking allocation regressions as new format periods are added.
/// </summary>
[MemoryDiagnoser]
public class HoldingsDataSetClientBenchmarks {
    private static readonly DateTime FullBackfillStart = new(2013, 1, 1);
    private static readonly DateTime IncrementalStart = DateTime.UtcNow.AddYears(-1);

    [Benchmark(Baseline = true)]
    public List<string> FullBackfill() => HoldingsDataSetClient.GetDataSetFileNames(FullBackfillStart);

    [Benchmark]
    public List<string> IncrementalSync() => HoldingsDataSetClient.GetDataSetFileNames(IncrementalStart);
}
