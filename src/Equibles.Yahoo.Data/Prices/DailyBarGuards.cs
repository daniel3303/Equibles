namespace Equibles.Yahoo.Data.Prices;

// The write guards every daily-bar writer applies to the shared price store, so a second source
// cannot store a bar the Yahoo lane would have refused or reconcile one on a different basis.
public static class DailyBarGuards
{
    // numeric(18,4) ceiling of the stored price columns.
    public const decimal MaxPriceValue = 99_999_999_999_999.9999m;

    // Relative half-width of the same-basis close comparison, wide enough for a revised close and
    // far below any split ratio.
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

    // A split moves price and volume by the same ratio in opposite directions, so two records of
    // one session are on the same basis only when their closes agree within a minor revision; the
    // guard is direction-agnostic because the store and the feed disagree in both orderings.
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
