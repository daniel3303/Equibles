using BenchmarkDotNet.Attributes;
using Equibles.Yahoo.Repositories;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// Per-stock cost of the MACD calculation in <see cref="TechnicalIndicatorService"/>. MACD
/// is the most expensive of the indicator family — it composes two EMAs plus a signal EMA
/// over their difference — and it runs over the full daily price history every time a stock's
/// chart view is rendered. The fixture is a 1 008-day series (~four trading years) shaped as a
/// slow drift plus a sinusoidal component, so the EMA recurrence stays non-degenerate.
/// </summary>
[MemoryDiagnoser]
public class TechnicalIndicatorServiceBenchmarks
{
    private const int PriceCount = 1_008;
    private List<decimal> _prices;

    [GlobalSetup]
    public void Setup()
    {
        // 100 + small daily drift + sinusoidal oscillation. Flat input would degenerate the
        // EMA recurrence; pure random would make cross-commit comparisons too noisy.
        _prices = new List<decimal>(PriceCount);
        for (var i = 0; i < PriceCount; i++)
        {
            var drift = i * 0.05m;
            var wave = (decimal)Math.Sin(i / 12.0) * 3m;
            _prices.Add(100m + drift + wave);
        }
    }

    [Benchmark]
    public int ComputeMacd()
    {
        var (line, signal, histogram) = TechnicalIndicatorService.ComputeMacd(_prices);
        return line.Count + signal.Count + histogram.Count;
    }
}
