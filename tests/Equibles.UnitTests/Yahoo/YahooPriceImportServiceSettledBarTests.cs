using System.Reflection;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins <c>YahooPriceImportService.IsSettledDailyBar</c>, the guard that keeps the current,
/// still-open trading day out of the daily series. Yahoo's daily chart returns the in-progress
/// session as a live candle (partial OHLC + partial volume); the importer is insert-only, so a
/// stored partial bar freezes and the real close never overwrites it. The guard must admit only
/// bars strictly before the current date. Both the boundary (today's bar excluded) and the
/// comparison direction are pinned: a flip to <c>&lt;=</c> re-admits the live candle, and a flip to
/// <c>&gt;</c> would drop the entire settled history and store only today.
/// </summary>
public class YahooPriceImportServiceSettledBarTests
{
    private static readonly MethodInfo IsSettledDailyBarMethod =
        typeof(YahooPriceImportService).GetMethod(
            "IsSettledDailyBar",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static bool IsSettledDailyBar(DateOnly barDate, DateOnly today) =>
        (bool)IsSettledDailyBarMethod.Invoke(null, [barDate, today]);

    private static readonly DateOnly Today = new(2026, 7, 2);

    [Fact]
    public void IsSettledDailyBar_CurrentDayBar_IsExcluded()
    {
        // The bug this fixes: a bar dated today is the in-progress session — a partial candle whose
        // "Close" is an intraday snapshot. It must not be persisted as a completed daily close.
        IsSettledDailyBar(Today, Today).Should().BeFalse();
    }

    [Fact]
    public void IsSettledDailyBar_PriorDayBar_IsKept()
    {
        // A bar for any prior date is a settled session and belongs in the series. Pinned alongside
        // the exclusion case so a regression that flips the comparison to `>` (which would drop the
        // whole settled history and keep only today) fails here.
        IsSettledDailyBar(Today.AddDays(-1), Today).Should().BeTrue();
    }

    [Fact]
    public void IsSettledDailyBar_FutureDatedBar_IsExcluded()
    {
        // Defensive: a bar dated after today (a clock skew or a bad exchange-offset calculation)
        // is never a settled bar and must be rejected, not stored ahead of the calendar.
        IsSettledDailyBar(Today.AddDays(1), Today).Should().BeFalse();
    }
}
