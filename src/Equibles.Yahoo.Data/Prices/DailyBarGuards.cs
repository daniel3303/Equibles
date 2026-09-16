namespace Equibles.Yahoo.Data.Prices;

// The write guards every daily-bar writer applies to the shared price store, so a second source
// cannot store a bar the Yahoo lane would have refused or reconcile one on a different basis.
public static class DailyBarGuards
{
    // numeric(18,4) ceiling of the stored price columns.
    public const decimal MaxPriceValue = 99_999_999_999_999.9999m;

    // Relative half-width of the same-basis close comparison; full rationale on IsSameSplitBasis.
    public const decimal SameBasisCloseTolerance = 0.01m;

    // One last-digit tick of absolute headroom on top of the relative tolerance. Both closes are
    // rounded to 4 decimals at ingest, so a genuine minor revision of a sub-cent close moves it by
    // a full 0.0001, more than 1% of the price, and a purely relative tolerance would freeze the
    // resettle out of the OTC tail. One tick stays orders of magnitude below any split ratio.
    public const decimal SameBasisCloseTickHeadroom = 0.0001m;

    // A quartet is storable only when every price is positive and the high and low bracket the
    // open and close; anything else is an impossible candle, whatever the source.
    public static bool IsValidOhlc(decimal open, decimal high, decimal low, decimal close) =>
        open > 0
        && high > 0
        && low > 0
        && close > 0
        && high >= open
        && high >= close
        && low <= open
        && low <= close
        && high >= low;

    // A full candle also has to fit the stored precision and carry a non-negative volume.
    public static bool IsValidCandle(
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume
    ) =>
        IsValidOhlc(open, high, low, close)
        && volume >= 0
        && !ExceedsPriceRange(open)
        && !ExceedsPriceRange(high)
        && !ExceedsPriceRange(low)
        && !ExceedsPriceRange(close);

    public static bool ExceedsPriceRange(decimal price) => Math.Abs(price) > MaxPriceValue;

    // Two records of the same session are only comparable when they are on the same split basis,
    // and the close is what proves it: a split moves price and volume by the SAME ratio in
    // opposite directions, so a basis mismatch shows up as a close that differs by that ratio.
    //
    // The stored series and the feed genuinely disagree here, in BOTH orderings, so the guard
    // stays direction-agnostic: before a reconcile the stored pre-split rows are still as-traded
    // while the feed already serves them adjusted, and after one the feed can go back to serving
    // the window as-traded (observed on WLFC's 3:1). Which basis each side holds varies by stock
    // and over time, so only this value comparison is safe; a mismatch means skip, never rewrite.
    //
    // Tolerance: both closes are rounded to 4 decimals at ingest, so same-basis values differ only
    // by a genuine minor revision, well inside 1%, while the split ratios Yahoo emits for real
    // splits (5:4 = 25%, 21:20 = 4.76%) sit far outside it. The one family inside the tolerance is
    // a tiny stock dividend recorded as a split (101:100 = 0.99%); accepting it bounds the volume
    // error at about 1%, negligible against the 10-29% unsettled shortfall the resettle fixes.
    public static bool IsSameSplitBasis(decimal storedClose, decimal fetchedClose)
    {
        // Nothing to compare against, so the basis is unproven rather than matching, and a zero
        // stored close would collapse the relative tolerance to exact equality.
        if (storedClose <= 0m || fetchedClose <= 0m)
            return false;

        return Math.Abs(fetchedClose - storedClose)
            <= storedClose * SameBasisCloseTolerance + SameBasisCloseTickHeadroom;
    }

    // Settled volume only ever accrues, so a fetched figure below the stored one is a degraded
    // response (a partial re-serve, a venue dropping out), never a correction.
    public static bool IsVolumeUpgrade(long stored, long fetched) => fetched > stored;

    // A daily chart includes the current, still-open session as a live candle whose figures keep
    // changing until the close, so only bars strictly before the current UTC date are settled.
    public static bool IsSettledDailyBar(DateOnly barDate, DateOnly today) => barDate < today;
}
