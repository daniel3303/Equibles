using BenchmarkDotNet.Attributes;
using Equibles.Web.Services;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// Per-stock cost of <see cref="TechnicalIndicatorService.ComputeRsi"/>. RSI sits on the same
/// per-chart-render hot path as MACD (already covered by <see cref="TechnicalIndicatorServiceBenchmarks"/>),
/// but its allocation shape is different — two pre-sized <see cref="decimal"/> arrays for the
/// gain/loss series, no nested EMA recurrence — so its cost should be tracked on its own.
/// The price fixture matches the MACD benchmark byte-for-byte so the two numbers are
/// directly comparable across commits.
/// </summary>
[MemoryDiagnoser]
public class TechnicalIndicatorServiceRsiBenchmarks
{
    private const int PriceCount = 1_008;
    private List<decimal> _prices;

    [GlobalSetup]
    public void Setup()
    {
        // Identical shape to TechnicalIndicatorServiceBenchmarks: ~4 trading years of slow
        // drift plus a sinusoidal component. Flat input would push every change to zero and
        // hit the avgLoss == 0 branch every step; pure random would make cross-commit deltas
        // dominated by noise.
        _prices = new List<decimal>(PriceCount);
        for (var i = 0; i < PriceCount; i++)
        {
            var drift = i * 0.05m;
            var wave = (decimal)Math.Sin(i / 12.0) * 3m;
            _prices.Add(100m + drift + wave);
        }
    }

    [Benchmark]
    public int ComputeRsi() => TechnicalIndicatorService.ComputeRsi(_prices).Count;
}
