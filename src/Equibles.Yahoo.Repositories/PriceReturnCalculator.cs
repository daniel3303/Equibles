namespace Equibles.Yahoo.Repositories;

/// <summary>
/// Computes trailing- and calendar-window returns from a daily close series.
/// Inputs are parallel lists ordered oldest→newest: <paramref name="dates"/>[i]
/// is the trading date of <paramref name="closes"/>[i]. All returns are
/// percentages relative to the window's base close.
/// </summary>
public static class PriceReturnCalculator
{
    private const int RoundingDigits = 2;

    public static PriceReturns Compute(IReadOnlyList<DateOnly> dates, IReadOnlyList<decimal> closes)
    {
        if (dates.Count != closes.Count)
            throw new ArgumentException("dates and closes must have the same length");

        var result = new PriceReturns();
        if (closes.Count == 0)
            return result;

        var lastClose = closes[^1];
        var lastDate = dates[^1];

        result.FiveDay = TrailingReturn(closes, lastClose, 5);
        result.TwentyDay = TrailingReturn(closes, lastClose, 20);
        result.OneHundredTwentyDay = TrailingReturn(closes, lastClose, 120);
        // MTD/YTD anchor on the prior period's final close: the last bar before
        // the current month / year began.
        result.MonthToDate = CalendarReturn(
            dates,
            closes,
            lastClose,
            new DateOnly(lastDate.Year, lastDate.Month, 1)
        );
        result.YearToDate = CalendarReturn(
            dates,
            closes,
            lastClose,
            new DateOnly(lastDate.Year, 1, 1)
        );
        return result;
    }

    // Compare the latest close to the one `days` bars earlier. Needs at least
    // days+1 bars; null otherwise.
    private static decimal? TrailingReturn(
        IReadOnlyList<decimal> closes,
        decimal lastClose,
        int days
    )
    {
        var baseIndex = closes.Count - 1 - days;
        return baseIndex < 0 ? null : Percent(closes[baseIndex], lastClose);
    }

    // Return from the last close strictly before `threshold` to the latest close.
    // Null when the series has no bar before the threshold (no prior-period close).
    private static decimal? CalendarReturn(
        IReadOnlyList<DateOnly> dates,
        IReadOnlyList<decimal> closes,
        decimal lastClose,
        DateOnly threshold
    )
    {
        for (var i = dates.Count - 1; i >= 0; i--)
        {
            if (dates[i] < threshold)
                return Percent(closes[i], lastClose);
        }
        return null;
    }

    // (current / baseValue - 1) × 100, rounded. A non-positive base can't yield a
    // meaningful percentage change, so it returns null.
    private static decimal? Percent(decimal baseValue, decimal current)
    {
        return baseValue <= 0
            ? null
            : Math.Round((current / baseValue - 1m) * 100m, RoundingDigits);
    }
}
