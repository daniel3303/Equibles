namespace Equibles.Yahoo.Data.Prices;

// The stored share count and market cap are read together at one moment's price, while the close
// shown beside them moves every session. One rule for every writer keeps the cap on the newest
// stored close, so a reader never sees a cap that is a session (or weeks) behind its price.
public static class MarketCapitalizationBasis
{
    // Headroom for the 4-decimal rounding of stored highs and lows around the feed's own price.
    public const double RangeTolerance = 0.002;

    // How far before a snapshot its last traded bar is looked for; covers the longest exchange holidays.
    public const int SnapshotLookbackDays = 14;

    /// <summary>
    /// The market cap restated on the newest traded close: shares × close, in major units. Null
    /// when the stored pair is not provably a statement at a price this listing traded at since
    /// the snapshot (a depositary ratio, another currency, a count from a different source):
    /// arithmetic on such a pair would invent a figure, so it stands until the next snapshot.
    /// </summary>
    public static double? OnNewestClose(
        long sharesOutstanding,
        double marketCapitalization,
        decimal? quoteUnitMultiplier,
        DateOnly snapshotDate,
        IReadOnlyCollection<TradedBar> bars
    )
    {
        var multiplier = (double)(quoteUnitMultiplier ?? 1m);
        if (
            sharesOutstanding <= 0
            || !(marketCapitalization > 0)
            || multiplier <= 0
            || bars == null
            || bars.Count == 0
        )
            return null;

        // The snapshot can predate its own session's bar, so the last bar before it counts too.
        var before = bars.Where(bar => bar.Date < snapshotDate)
            .Select(bar => (DateOnly?)bar.Date)
            .Max();
        var window = bars.Where(bar => bar.Date >= (before ?? snapshotDate)).ToList();
        if (window.Count == 0)
            return null;

        var impliedPrice = marketCapitalization / (sharesOutstanding * multiplier);
        var traded = window.Any(bar =>
            impliedPrice >= (double)bar.Low * (1 - RangeTolerance)
            && impliedPrice <= (double)bar.High * (1 + RangeTolerance)
        );
        if (!traded)
            return null;

        var newest = window.MaxBy(bar => bar.Date);
        return newest.Close > 0 ? sharesOutstanding * (double)newest.Close * multiplier : null;
    }
}
