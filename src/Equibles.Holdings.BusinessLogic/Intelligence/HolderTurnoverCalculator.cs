using Equibles.Holdings.BusinessLogic.Intelligence.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// A filer's equity trades between consecutive quarters, priced by each security's quarter average
/// price when known, else its quarter-end value per share (an exit's from the quarter before).
/// </summary>
/// <remarks>
/// Invariant: a trade is worth at most the larger of its two positions at the trade's price, and
/// each position at that price stays within <see cref="HoldingValueDisagreement.CapMultiple"/> of
/// the value it reports, the gap being the price move since its quarter end. A position past the
/// cap holds shares on another unit or basis than its value (swapped columns, a sibling class's
/// CUSIP), so its trade is unpriced.
/// </remarks>
public static class HolderTurnoverCalculator
{
    /// <summary>
    /// <paramref name="previous"/> (null without a 13F the quarter before) and
    /// <paramref name="averagePrices"/> (a price of zero or less is missing) are on the current
    /// quarter's share basis. A security held on both sides in <paramref name="unresolvedBasis"/>,
    /// or whose positions break the invariant, is an unpriced trade of unknown direction.
    /// </summary>
    public static HolderTradeSummary Trades(
        IReadOnlyList<HolderEquityPosition> current,
        IReadOnlyList<HolderEquityPosition> previous,
        IReadOnlyDictionary<HolderSecurityKey, decimal> averagePrices = null,
        IReadOnlySet<HolderSecurityKey> unresolvedBasis = null
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
        var trades = new List<HolderSecurityTrade>();
        int news = 0,
            exits = 0,
            increases = 0,
            reductions = 0,
            unpriced = 0;
        foreach (var security in currentBySecurity.Keys.Union(previousBySecurity.Keys))
        {
            currentBySecurity.TryGetValue(security, out var now);
            previousBySecurity.TryGetValue(security, out var before);
            if (now != null && before != null && unresolvedBasis?.Contains(security) == true)
            {
                unpriced++;
                continue;
            }
            var traded = (now?.Shares ?? 0) - (before?.Shares ?? 0);
            if (traded == 0)
                continue;

            // Only an exit, which has no row this quarter, falls back to the quarter before.
            var price =
                averagePrices != null
                && averagePrices.TryGetValue(security, out var average)
                && average > 0
                    ? average
                : now == null ? PerShare(before)
                : PerShare(now);
            var plausible =
                price == null || (Vouches(now, price.Value) && Vouches(before, price.Value));
            if (now != null && before != null && !plausible)
            {
                unpriced++;
                continue;
            }

            if (before == null)
                news++;
            else if (now == null)
                exits++;
            else if (traded > 0)
                increases++;
            else
                reductions++;
            if (price == null || !plausible)
            {
                unpriced++;
                continue;
            }
            var tradeValue = Math.Abs(traded) * price.Value;
            if (traded > 0)
                buys += tradeValue;
            else
                sells += tradeValue;
            trades.Add(
                new HolderSecurityTrade(
                    security,
                    traded > 0 ? tradeValue : 0,
                    traded < 0 ? tradeValue : 0
                )
            );
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
            SecurityTrades = trades,
        };
    }

    /// <summary>
    /// The sum of the last four quarterly turnovers, or null unless all four are known.
    /// </summary>
    public static decimal? TrailingAnnual(IReadOnlyList<decimal?> lastFourQuarters) =>
        lastFourQuarters is { Count: 4 } && lastFourQuarters.All(turnover => turnover != null)
            ? lastFourQuarters.Sum(turnover => turnover.GetValueOrDefault())
            : null;

    // Rows of one security are summed; a value pending on any of them leaves the security unvalued,
    // and a row that reports no value at all leaves the security's shares unvouched.
    private static Dictionary<HolderSecurityKey, SecurityBook> BySecurity(
        IReadOnlyList<HolderEquityPosition> positions
    ) =>
        positions
            .Where(position => position.Shares > 0)
            .GroupBy(position => position.Security)
            .ToDictionary(
                group => group.Key,
                group => new SecurityBook(
                    group.Sum(position => position.Shares),
                    group.All(position => position.Value != null)
                        ? group.Sum(position => position.Value.GetValueOrDefault())
                        : null,
                    group.All(position => position.ReportedValue != null)
                        ? group.Sum(position => position.ReportedValue.GetValueOrDefault())
                        : null
                )
            );

    // The invariant for one side: its shares at the trade's price within the cap of the value it
    // reports. A side that reports no value cannot vouch for its shares.
    private static bool Vouches(SecurityBook book, decimal price) =>
        book == null
        || book.Reported is { } reported
            && !HoldingValueDisagreement.GrosslyExceeds(book.Shares * price, reported);

    private static long PricedValue(IEnumerable<SecurityBook> books) =>
        books.Where(book => book.Value > 0).Sum(book => book.Value.GetValueOrDefault());

    private static decimal? PerShare(SecurityBook book) =>
        book is { Value: long value and > 0, Shares: > 0 } ? (decimal)value / book.Shares : null;

    // One security's summed rows: the published value counts toward the book, the reported value
    // (published, else filed) vouches for the shares.
    private sealed record SecurityBook(long Shares, long? Value, long? Reported);
}
