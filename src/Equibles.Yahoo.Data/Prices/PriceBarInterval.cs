using System.Globalization;
using Equibles.Yahoo.Data.Models;

namespace Equibles.Yahoo.Data.Prices;

public enum PriceBarInterval
{
    Daily,
    Weekly,
    Monthly,
}

// One aggregated bar: labelled by its last traded session, and Partial when the requested window
// cuts its period, so a still-open week or month never reads as a settled one.
public sealed record PriceBar(
    DateOnly Date,
    DateOnly PeriodStart,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal AdjustedClose,
    long Volume,
    bool Partial
);

// Rolls daily bars into ISO weeks or calendar months: the first open, the highest high, the lowest
// low, the last close and adjusted close, and the summed volume. Raw rows carry no split basis, so
// a bucket inherits whatever basis its daily rows have.
public static class PriceBarAggregation
{
    public static bool TryParse(string value, out PriceBarInterval interval)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "daily" or "day" or "1d":
                interval = PriceBarInterval.Daily;
                return true;
            case "weekly" or "week" or "1wk":
                interval = PriceBarInterval.Weekly;
                return true;
            case "monthly" or "month" or "1mo":
                interval = PriceBarInterval.Monthly;
                return true;
            default:
                interval = PriceBarInterval.Daily;
                return false;
        }
    }

    // Bars oldest first. The window is the requested [start, end] after any release hold, so a
    // bucket the window starts or ends inside is marked Partial.
    public static List<PriceBar> Aggregate(
        IEnumerable<EquityDailyStockPrice> dailyBars,
        PriceBarInterval interval,
        DateOnly start,
        DateOnly end
    ) =>
        dailyBars
            .OrderBy(bar => bar.Date)
            .GroupBy(bar => PeriodStart(bar.Date, interval))
            .Select(bucket =>
            {
                var bars = bucket.ToList();
                var periodStart = bucket.Key;
                var periodEnd = PeriodEnd(periodStart, interval);
                return new PriceBar(
                    bars[^1].Date,
                    periodStart,
                    bars[0].Open,
                    bars.Max(bar => bar.High),
                    bars.Min(bar => bar.Low),
                    bars[^1].Close,
                    bars[^1].AdjustedClose,
                    bars.Sum(bar => bar.Volume),
                    start > FirstWeekday(periodStart, periodEnd)
                        || end < LastWeekday(periodStart, periodEnd)
                );
            })
            .ToList();

    public static DateOnly PeriodStart(DateOnly date, PriceBarInterval interval) =>
        interval switch
        {
            PriceBarInterval.Weekly => DateOnly.FromDateTime(
                ISOWeek.ToDateTime(
                    ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue)),
                    ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue)),
                    DayOfWeek.Monday
                )
            ),
            PriceBarInterval.Monthly => new DateOnly(date.Year, date.Month, 1),
            _ => date,
        };

    private static DateOnly PeriodEnd(DateOnly periodStart, PriceBarInterval interval) =>
        interval switch
        {
            PriceBarInterval.Weekly => periodStart.AddDays(6),
            PriceBarInterval.Monthly => periodStart.AddMonths(1).AddDays(-1),
            _ => periodStart,
        };

    // A window that starts on the weekend before the period's first weekday, or ends on the weekend
    // after its last one, still covers every session of the period.
    private static DateOnly FirstWeekday(DateOnly periodStart, DateOnly periodEnd)
    {
        var day = periodStart;
        while (day < periodEnd && IsWeekend(day))
            day = day.AddDays(1);
        return day;
    }

    private static DateOnly LastWeekday(DateOnly periodStart, DateOnly periodEnd)
    {
        var day = periodEnd;
        while (day > periodStart && IsWeekend(day))
            day = day.AddDays(-1);
        return day;
    }

    private static bool IsWeekend(DateOnly day) =>
        day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}
