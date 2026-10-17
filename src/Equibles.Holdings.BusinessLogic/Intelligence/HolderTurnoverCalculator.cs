using Equibles.Holdings.BusinessLogic.Intelligence.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// A filer's equity trades between consecutive quarters, priced by each security's quarter average
/// price when known, else its quarter-end value per share (an exit's from the quarter before).
/// </summary>
public static class HolderTurnoverCalculator
{
    /// <summary>
    /// <paramref name="previous"/> is null when the filer filed no 13F the quarter before; its shares
    /// must already be restated onto the current quarter's basis. <paramref name="averagePrices"/> are
    /// per share on that same basis, so a stored close series restated for a later split must be
    /// put back on the quarter's basis first; a price of zero or less counts as missing.
    /// </summary>
    public static HolderTradeSummary Trades(
        IReadOnlyList<HolderEquityPosition> current,
        IReadOnlyList<HolderEquityPosition> previous,
        IReadOnlyDictionary<HolderSecurityKey, decimal> averagePrices = null
    )
    {
        var currentBySecurity = BySecurity(current);
        var equityAum = PricedValue(currentBySecurity.Values);
        if (previous == null)
            return new HolderTradeSummary { EquityAum = equityAum };

        var previousBySecurity = BySecurity(previous);
        var previousEquityAum = PricedValue(previousBySecurity.Values);
        decimal buys = 0,
            sells = 0;
        int news = 0,
            exits = 0,
            increases = 0,
            reductions = 0,
            unpriced = 0;
        foreach (var security in currentBySecurity.Keys.Union(previousBySecurity.Keys))
        {
            currentBySecurity.TryGetValue(security, out var now);
            previousBySecurity.TryGetValue(security, out var before);
            var traded = (now?.Shares ?? 0) - (before?.Shares ?? 0);
            if (traded == 0)
                continue;
            if ((before?.Shares ?? 0) == 0)
                news++;
            else if ((now?.Shares ?? 0) == 0)
                exits++;
            else if (traded > 0)
                increases++;
            else
                reductions++;

            // Only an exit, which has no row this quarter, falls back to the quarter before.
            var price =
                averagePrices != null
                && averagePrices.TryGetValue(security, out var average)
                && average > 0
                    ? average
                : now == null ? PerShare(before)
                : PerShare(now);
            if (price == null)
            {
                unpriced++;
                continue;
            }
            if (traded > 0)
                buys += traded * price.Value;
            else
                sells += -traded * price.Value;
        }

        var averageAum = (equityAum + previousEquityAum) / 2m;
        return new HolderTradeSummary
        {
            EquityAum = equityAum,
            PreviousEquityAum = previousEquityAum,
            BuyValue = buys,
            SellValue = sells,
            Turnover = averageAum > 0 ? Math.Min(buys, sells) / averageAum : null,
            NewCount = news,
            ExitCount = exits,
            IncreasedCount = increases,
            ReducedCount = reductions,
            UnpricedTradeCount = unpriced,
        };
    }

    /// <summary>
    /// The sum of the last four quarterly turnovers, or null unless all four are known.
    /// </summary>
    public static decimal? TrailingAnnual(IReadOnlyList<decimal?> lastFourQuarters) =>
        lastFourQuarters is { Count: 4 } && lastFourQuarters.All(turnover => turnover != null)
            ? lastFourQuarters.Sum(turnover => turnover.Value)
            : null;

    // Rows of one security are summed; a value pending on any of them leaves the security unvalued.
    private static Dictionary<HolderSecurityKey, HolderEquityPosition> BySecurity(
        IReadOnlyList<HolderEquityPosition> positions
    ) =>
        positions
            .Where(position => position.Shares > 0)
            .GroupBy(position => position.Security)
            .ToDictionary(
                group => group.Key,
                group => new HolderEquityPosition(
                    group.Key,
                    group.Sum(position => position.Shares),
                    group.All(position => position.Value != null)
                        ? group.Sum(position => position.Value.Value)
                        : null
                )
            );

    private static long PricedValue(IEnumerable<HolderEquityPosition> positions) =>
        positions.Where(position => position.Value > 0).Sum(position => position.Value.Value);

    private static decimal? PerShare(HolderEquityPosition position) =>
        position is { Value: > 0, Shares: > 0 }
            ? (decimal)position.Value.Value / position.Shares
            : null;
}
