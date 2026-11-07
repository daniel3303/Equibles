using Equibles.CorporateActions.Data.Models;

namespace Equibles.CorporateActions.Data;

/// <summary>
/// One corporate event as several captured rows: sources and listings report the same split on
/// different days and sometimes with different ratios.
/// </summary>
public static class SplitEvents
{
    // Rows this close are one event reported on different days (announced vs executed, a moved
    // execution date); a real repeat of one ratio sits a month apart. The collapse migration carries the literal.
    public const int SameEventWindowDays = 10;

    // Two sources can date one sizable split further apart than SameEventWindowDays (a chart
    // provider's stale announced date beside the feed's executed date, three weeks on), and a
    // company never repeats a 3:2 within this many days. A near-unity ratio is excluded because
    // scheduled monthly 101:100 repeats are real events; the collapse migration carries both literals.
    public const int CrossSourceSameEventWindowDays = 45;
    public const decimal MinimumCrossSourceRatioShift = 0.25m;

    // Relative half-width within which two figures sit on one share basis: wide enough for a
    // revised close or ordinary share-count drift between filings, far below any split ratio.
    public const decimal SameBasisTolerance = 0.01m;

    /// <summary>Whether <paramref name="value"/> sits on the same share basis as <paramref name="reference"/>.</summary>
    public static bool IsSameBasis(decimal value, decimal reference) =>
        reference > 0m && Math.Abs(value - reference) <= reference * SameBasisTolerance;

    /// <summary>
    /// Groups rows into events, chaining rows whose effective dates sit within
    /// <see cref="SameEventWindowDays"/> of the previous row, across every listing and source.
    /// </summary>
    public static List<List<StockSplit>> Group(IEnumerable<StockSplit> splits)
    {
        var events = new List<List<StockSplit>>();
        foreach (
            var split in splits.OrderBy(split => split.EffectiveDate).ThenBy(split => split.Id)
        )
        {
            if (
                events.Count > 0
                && split.EffectiveDate.DayNumber - events[^1][^1].EffectiveDate.DayNumber
                    <= SameEventWindowDays
            )
                events[^1].Add(split);
            else
                events.Add([split]);
        }
        return events;
    }

    /// <summary>
    /// Whether a ratio can be a revised definition of another: a different ratio that moves the
    /// share count the same way. A postponed reverse split revised from 1:40 to 1:30 is still a
    /// reverse split; a forward split beside a reverse consolidation is two events.
    /// </summary>
    public static bool IsRevisedRatio(
        decimal numerator,
        decimal denominator,
        decimal revisedNumerator,
        decimal revisedDenominator
    ) =>
        numerator > 0m
        && denominator > 0m
        && revisedNumerator > 0m
        && revisedDenominator > 0m
        && numerator * revisedDenominator != revisedNumerator * denominator
        && Math.Sign(numerator - denominator) != 0
        && Math.Sign(numerator - denominator) == Math.Sign(revisedNumerator - revisedDenominator);

    /// <summary>
    /// Whether a stored row and a captured observation of the same ratio on one listing describe
    /// one event: within <see cref="SameEventWindowDays"/> always, and within
    /// <see cref="CrossSourceSameEventWindowDays"/> when they come from different sources and the
    /// ratio moves the share count by at least <see cref="MinimumCrossSourceRatioShift"/>.
    /// </summary>
    public static bool IsSameEvent(
        DateOnly date,
        StockSplitSource source,
        DateOnly otherDate,
        StockSplitSource otherSource,
        decimal numerator,
        decimal denominator
    )
    {
        var daysApart = Math.Abs(date.DayNumber - otherDate.DayNumber);
        if (daysApart <= SameEventWindowDays)
            return true;
        return source != otherSource
            && daysApart <= CrossSourceSameEventWindowDays
            && denominator > 0m
            && Math.Abs(numerator / denominator - 1m) >= MinimumCrossSourceRatioShift;
    }

    /// <summary>The row's ratio, or null when an arm is not positive and it cannot describe a split.</summary>
    public static decimal? Ratio(StockSplit split) =>
        split.Numerator > 0m && split.Denominator > 0m ? split.Numerator / split.Denominator : null;
}
