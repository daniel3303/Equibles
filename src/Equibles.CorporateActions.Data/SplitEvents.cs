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

    /// <summary>The row's ratio, or null when an arm is not positive and it cannot describe a split.</summary>
    public static decimal? Ratio(StockSplit split) =>
        split.Numerator > 0m && split.Denominator > 0m ? split.Numerator / split.Denominator : null;
}
