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

    // A close-to-close move a split could explain. A bar can be stored before its split's record,
    // and the count stored here would then be the pre-split one, so a pair is never carried
    // across a larger move: it stands until the feed states it again.
    public const double MaxSessionMove = 0.15;

    /// <summary>
    /// The market cap restated on the newest traded close: shares × close, in major units. Null
    /// when the stored pair is not provably a statement at a price this listing traded at around
    /// its snapshot (a depositary ratio, another currency, a count from a different source), or
    /// when a session since that price moved by more than <see cref="MaxSessionMove"/>: arithmetic
    /// on such a pair would invent a figure, so it stands until the next snapshot.
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

        var lookback = bars.Where(bar =>
                bar.Date >= snapshotDate.AddDays(-SnapshotLookbackDays) && bar.Close > 0
            )
            .OrderBy(bar => bar.Date)
            .ToList();
        if (lookback.Count == 0)
            return null;

        var impliedPrice = marketCapitalization / (sharesOutstanding * multiplier);
        // A pair this rule wrote sits exactly on a stored close. That holds whichever bars land
        // afterwards, while the snapshot's window moves when an earlier session's bar is stored
        // late, so the close is looked for across the whole lookback.
        var onClose = lookback.FindLastIndex(bar =>
            Math.Abs(impliedPrice - (double)bar.Close) <= (double)bar.Close * RangeTolerance
        );
        // The feed's own pair is at its live price: inside the range of the last bar before the
        // snapshot (the snapshot can predate its own session's bar) or of a later one.
        var before = lookback.FindLastIndex(bar => bar.Date < snapshotDate);
        var inRange = lookback.FindIndex(
            Math.Max(before, 0),
            bar =>
                impliedPrice >= (double)bar.Low * (1 - RangeTolerance)
                && impliedPrice <= (double)bar.High * (1 + RangeTolerance)
        );
        var proven = Math.Max(onClose, inRange);
        if (proven < 0)
            return null;

        for (var index = proven + 1; index < lookback.Count; index++)
        {
            var move = (double)lookback[index].Close / (double)lookback[index - 1].Close - 1;
            if (Math.Abs(move) > MaxSessionMove)
                return null;
        }
        return sharesOutstanding * (double)lookback[^1].Close * multiplier;
    }
}
