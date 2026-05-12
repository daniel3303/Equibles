using BenchmarkDotNet.Attributes;
using Equibles.Web.Services;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// Per-stock cost of <see cref="TechnicalIndicatorService.ComputeEma"/>. EMA is the building
/// block <see cref="TechnicalIndicatorService.ComputeMacd"/> composes three times (fast EMA,
/// slow EMA, signal EMA over the difference), so having a standalone EMA number alongside the
/// already-benchmarked MACD lets us reason about how that composition cost decomposes — if EMA
/// regresses, MACD will regress; if MACD regresses but EMA doesn't, the diff is in the glue.
/// Price fixture matches the MACD and RSI benchmarks byte-for-byte for direct cost comparison
/// across all three indicators.
/// </summary>
[MemoryDiagnoser]
public class TechnicalIndicatorServiceEmaBenchmarks {
    private const int PriceCount = 1_008;
    private const int Period = 26;
    private List<decimal> _prices;

    [GlobalSetup]
    public void Setup() {
        // Identical shape to TechnicalIndicatorServiceBenchmarks/TechnicalIndicatorServiceRsiBenchmarks:
        // ~4 trading years of slow drift plus a sinusoidal component. The 26-period matches
        // MACD's slow EMA, so this benchmark measures one of the exact instances MACD composes.
        _prices = new List<decimal>(PriceCount);
        for (var i = 0; i < PriceCount; i++) {
            var drift = i * 0.05m;
            var wave = (decimal)Math.Sin(i / 12.0) * 3m;
            _prices.Add(100m + drift + wave);
        }
    }

    [Benchmark]
    public int ComputeEma() => TechnicalIndicatorService.ComputeEma(_prices, Period).Count;
}
