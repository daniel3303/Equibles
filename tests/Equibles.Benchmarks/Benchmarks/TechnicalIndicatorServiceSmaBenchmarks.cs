using BenchmarkDotNet.Attributes;
using Equibles.Web.Services;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// Per-stock cost of <see cref="TechnicalIndicatorService.ComputeSma"/>. SMA is the only
/// indicator <see cref="TechnicalIndicatorService"/> exposes that does not have a dedicated
/// benchmark yet (EMA/RSI/MACD all have one). The price-tab view computes three SMAs per
/// render — SMA20, SMA50, SMA200 — and the implementation is a nested O(N·period) loop, so
/// SMA200 dominates: ~5× the inner-loop work of SMA50 and ~10× of SMA20. This benchmark
/// pins the worst-case period the production view uses, giving us a number to alarm on if a
/// refactor (e.g. moving to a rolling sum) regresses the hot path. Price fixture matches the
/// MACD/EMA/RSI benchmarks byte-for-byte for direct cost comparison across all four
/// indicators.
/// </summary>
[MemoryDiagnoser]
public class TechnicalIndicatorServiceSmaBenchmarks {
    private const int PriceCount = 1_008;
    private const int Period = 200;
    private List<decimal> _prices;

    [GlobalSetup]
    public void Setup() {
        // Identical shape to TechnicalIndicatorServiceBenchmarks/TechnicalIndicatorServiceEmaBenchmarks/
        // TechnicalIndicatorServiceRsiBenchmarks: ~4 trading years of slow drift plus a sinusoidal
        // component. Keeps the SMA arithmetic numerically meaningful (no flat input that would
        // make every window sum identical) without introducing run-to-run noise.
        _prices = new List<decimal>(PriceCount);
        for (var i = 0; i < PriceCount; i++) {
            var drift = i * 0.05m;
            var wave = (decimal)Math.Sin(i / 12.0) * 3m;
            _prices.Add(100m + drift + wave);
        }
    }

    [Benchmark]
    public int ComputeSma() => TechnicalIndicatorService.ComputeSma(_prices, Period).Count;
}
