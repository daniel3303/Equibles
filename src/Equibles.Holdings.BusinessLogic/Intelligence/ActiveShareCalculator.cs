namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// Active share: half the summed absolute weight differences between a portfolio and a benchmark
/// over the union of their holdings, from 0 (the benchmark) to 1 (no overlap).
/// </summary>
public static class ActiveShareCalculator
{
    /// <summary>
    /// Each side is normalised to sum to one, and null when empty. Benchmark holdings the portfolio
    /// lacks contribute one minus the weight its holdings take of the benchmark, so the differences
    /// are summed over the portfolio's holdings only.
    /// </summary>
    public static decimal? ActiveShare<TKey>(
        IReadOnlyDictionary<TKey, decimal> portfolio,
        IReadOnlyDictionary<TKey, decimal> benchmark
    )
    {
        var portfolioTotal = portfolio.Values.Where(value => value > 0).Sum();
        var benchmarkTotal = benchmark.Values.Where(value => value > 0).Sum();
        if (portfolioTotal <= 0 || benchmarkTotal <= 0)
            return null;

        decimal difference = 0,
            sharedBenchmarkWeight = 0;
        foreach (var (key, value) in portfolio)
        {
            if (value <= 0)
                continue;
            var benchmarkWeight =
                benchmark.TryGetValue(key, out var raw) && raw > 0 ? raw / benchmarkTotal : 0;
            difference += Math.Abs(value / portfolioTotal - benchmarkWeight);
            sharedBenchmarkWeight += benchmarkWeight;
        }
        return Math.Clamp((difference + 1 - sharedBenchmarkWeight) / 2, 0, 1);
    }
}
