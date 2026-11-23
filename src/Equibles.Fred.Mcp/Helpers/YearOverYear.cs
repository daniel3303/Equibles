using Equibles.Fred.Data.Models;
using Equibles.Fred.Mcp.Tools;

namespace Equibles.Fred.Mcp.Helpers;

/// <summary>
/// An observation's change from a year earlier, as FRED's own year-ago units read it: a percent change for a level, a
/// difference in percentage points for a series already stated in percent. A series whose units are already a change
/// has no meaningful year-ago change.
/// </summary>
public static class YearOverYear
{
    // A daily series closes on holidays and weekends: its year-earlier base is the last observation in the week up to
    // the same date a year earlier.
    private const int DailyLookbackDays = 7;

    // A weekly series compares the same weekday 52 weeks back, which is a day or two later than the same date.
    private const int FiftyTwoWeeksDays = 364;

    public static bool AlreadyAChange(string units) =>
        units?.Contains("change", StringComparison.OrdinalIgnoreCase) == true;

    public static bool InPoints(string units) =>
        units?.TrimStart().StartsWith("percent", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The first date a base for <paramref name="date"/> can carry.</summary>
    public static DateOnly EarliestBaseDate(DateOnly date) =>
        date.AddYears(-1).AddDays(-DailyLookbackDays);

    /// <summary>The last date a base for <paramref name="date"/> can carry: a weekly series' 52 weeks back.</summary>
    public static DateOnly LatestBaseDate(DateOnly date) => date.AddDays(-FiftyTwoWeeksDays);

    /// <summary>
    /// The stated observation a year before <paramref name="date"/>: the same date for monthly, quarterly and annual
    /// series, 52 weeks earlier for weekly ones, and for a daily series the last observation on or before the same
    /// date a year earlier, within a week. Null when stored data holds none.
    /// </summary>
    public static FredObservation Base(
        DateOnly date,
        string frequency,
        IReadOnlyDictionary<DateOnly, FredObservation> statedByDate
    )
    {
        // Series store FRED's short code ("D") or, from other publishers, a name ("Weekly, Ending Friday").
        var name = FredTools.FrequencyName(frequency);
        if (
            name.StartsWith("Weekly", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Biweekly", StringComparison.OrdinalIgnoreCase)
        )
            return statedByDate.GetValueOrDefault(date.AddDays(-FiftyTwoWeeksDays));
        if (name.StartsWith("Daily", StringComparison.OrdinalIgnoreCase))
        {
            var target = date.AddYears(-1);
            for (var day = target; day >= target.AddDays(-DailyLookbackDays); day = day.AddDays(-1))
            {
                if (statedByDate.TryGetValue(day, out var observation))
                    return observation;
            }
            return null;
        }
        return statedByDate.GetValueOrDefault(date.AddYears(-1));
    }

    /// <summary>
    /// The change from <paramref name="yearEarlier"/> to <paramref name="value"/>: percentage points when
    /// <paramref name="inPoints"/>, else a percent change, which needs a positive base. Null when either is missing.
    /// </summary>
    public static decimal? Change(decimal? value, decimal? yearEarlier, bool inPoints)
    {
        if (value is not { } now || yearEarlier is not { } then)
            return null;
        if (inPoints)
            return now - then;
        if (then <= 0)
            return null;
        return Math.Round((now / then - 1m) * 100m, 2, MidpointRounding.AwayFromZero);
    }
}
