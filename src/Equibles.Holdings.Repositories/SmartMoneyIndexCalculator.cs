using Equibles.Holdings.Repositories.Models;

namespace Equibles.Holdings.Repositories;

/// <summary>
/// Builds the constituent list of a "smart-money index" from the latest 13F portfolios of the
/// top-scoring funds. Selects the funds' highest-conviction common holdings — stocks held by at
/// least <c>minConsensus</c> of the funds — ranks them by consensus count then by average
/// portfolio weight, caps the list at <c>maxConstituents</c>, and equal-weights what remains.
/// <para>
/// Pure and listing-based: each input snapshot is one fund's latest portfolio (option rows and
/// non-positive values ignored, the same convention <see cref="HoldingsBacktestCalculator"/>
/// uses). The caller canonicalizes legacy-null primary attribution, resolves tickers/names, and
/// runs the backtest.
/// </para>
/// </summary>
public static class SmartMoneyIndexCalculator
{
    /// <summary>Default number of top-alpha funds to draw the consensus from.</summary>
    public const int DefaultTopFunds = 20;

    /// <summary>Default cap on the number of stocks in the index.</summary>
    public const int DefaultMaxConstituents = 25;

    /// <summary>Default minimum number of funds that must hold a stock for it to qualify.</summary>
    public const int DefaultMinConsensus = 3;

    public static List<SmartMoneyIndexConstituent> Compose(
        IReadOnlyList<BacktestQuarterSnapshot> fundPortfolios,
        int maxConstituents = DefaultMaxConstituents,
        int minConsensus = DefaultMinConsensus
    )
    {
        maxConstituents = Math.Max(1, maxConstituents);
        minConsensus = Math.Max(1, minConsensus);

        // Per exact listing: how many funds hold it, and the running sum of its per-fund weight.
        var accumulator = new Dictionary<SecurityKey, ConvictionAccumulator>();

        foreach (var fund in fundPortfolios)
        {
            // Collapse a fund's multiple discretion rows for the same listing into one position,
            // dropping options (notional) and non-positive values before weighting.
            var positions = fund
                .Positions.Where(p => !p.IsOption && p.Value > 0)
                .GroupBy(p => new SecurityKey(p.CommonStockId, p.ListedTicker))
                .Select(g => (Security: g.Key, Value: g.Sum(p => p.Value)))
                .ToList();

            var totalValue = positions.Sum(p => p.Value);
            if (totalValue <= 0)
                continue;

            foreach (var (security, value) in positions)
            {
                var weightPercent = (decimal)value / totalValue * 100m;
                if (accumulator.TryGetValue(security, out var current))
                {
                    current.HeldByCount++;
                    current.WeightSum += weightPercent;
                }
                else
                {
                    accumulator[security] = new ConvictionAccumulator
                    {
                        HeldByCount = 1,
                        WeightSum = weightPercent,
                    };
                }
            }
        }

        var selected = accumulator
            .Where(entry => entry.Value.HeldByCount >= minConsensus)
            .OrderByDescending(entry => entry.Value.HeldByCount)
            .ThenByDescending(entry => entry.Value.WeightSum / entry.Value.HeldByCount)
            .ThenBy(entry => entry.Key.CommonStockId)
            .ThenBy(entry => entry.Key.ListedTicker, StringComparer.Ordinal)
            .Take(maxConstituents)
            .ToList();

        if (selected.Count == 0)
            return [];

        var indexWeight = Math.Round(100m / selected.Count, 4);

        return selected
            .Select(entry => new SmartMoneyIndexConstituent
            {
                CommonStockId = entry.Key.CommonStockId,
                ListedTicker = entry.Key.ListedTicker,
                HeldByCount = entry.Value.HeldByCount,
                AverageWeightPercent = Math.Round(
                    entry.Value.WeightSum / entry.Value.HeldByCount,
                    4
                ),
                IndexWeightPercent = indexWeight,
            })
            .ToList();
    }

    private sealed class ConvictionAccumulator
    {
        public int HeldByCount { get; set; }
        public decimal WeightSum { get; set; }
    }

    private readonly record struct SecurityKey(Guid CommonStockId, string ListedTicker);
}
