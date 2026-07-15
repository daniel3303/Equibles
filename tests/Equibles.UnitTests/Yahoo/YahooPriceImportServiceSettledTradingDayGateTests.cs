using System.Reflection;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins <c>YahooPriceImportService.HasSettledTradingDay</c>, the gate that lets a price cycle
/// skip the chart fetch when no new settled bar can exist for a stock: only when at least one
/// NYSE trading day lies in [startDate, today) can Yahoo have a new row to return. The gate is
/// what makes frequent price cycles affordable — a current stock costs zero Yahoo calls until the
/// next session settles, and weekend/holiday cycles are no-ops for the whole universe. The
/// dangerous regression is a gate that returns false when a settled bar COULD exist (prices stop
/// updating silently), so the admitting cases are pinned as tightly as the skipping ones.
/// </summary>
public class YahooPriceImportServiceSettledTradingDayGateTests
{
    private static readonly MethodInfo HasSettledTradingDayMethod =
        typeof(YahooPriceImportService).GetMethod(
            "HasSettledTradingDay",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static bool HasSettledTradingDay(DateOnly startDate, DateOnly today) =>
        (bool)HasSettledTradingDayMethod.Invoke(null, [startDate, today]);

    [Fact]
    public void HasSettledTradingDay_StockAlreadyCurrent_Skips()
    {
        // startDate == today: the window is empty — nothing unsynced is settled yet. This is the
        // steady state of every fast cycle after the day's bar landed; it must cost no fetch.
        var tuesday = new DateOnly(2026, 7, 14);
        HasSettledTradingDay(tuesday, tuesday).Should().BeFalse();
    }

    [Fact]
    public void HasSettledTradingDay_StartDateAfterToday_Skips()
    {
        // Defensive: a start date past today (clock skew, MaxValue sentinel) yields an empty
        // window, not an infinite loop or a fetch.
        var tuesday = new DateOnly(2026, 7, 14);
        HasSettledTradingDay(tuesday.AddDays(1), tuesday).Should().BeFalse();
        HasSettledTradingDay(DateOnly.MaxValue, tuesday).Should().BeFalse();
    }

    [Fact]
    public void HasSettledTradingDay_WeekdaySettled_Fetches()
    {
        // Monday's bar settled, Tuesday cycle: [Mon, Tue) holds one trading day — must fetch.
        var monday = new DateOnly(2026, 7, 13);
        var tuesday = new DateOnly(2026, 7, 14);
        HasSettledTradingDay(monday, tuesday).Should().BeTrue();
    }

    [Fact]
    public void HasSettledTradingDay_WeekendWindow_Skips()
    {
        // Friday's bar stored, so startDate is Saturday. Cycles on Saturday, Sunday, and Monday
        // itself see only weekend dates in the window (Monday's own session is still live) —
        // every one of them must skip.
        var saturday = new DateOnly(2026, 7, 11);
        HasSettledTradingDay(saturday, new DateOnly(2026, 7, 12)).Should().BeFalse(); // Sunday
        HasSettledTradingDay(saturday, new DateOnly(2026, 7, 13)).Should().BeFalse(); // Monday
    }

    [Fact]
    public void HasSettledTradingDay_MondaySettled_TuesdayFetches()
    {
        // Same weekend window one day later: [Sat, Tue) now contains Monday's settled session, so
        // the skip must end exactly here — a gate that keeps skipping stops price updates cold.
        var saturday = new DateOnly(2026, 7, 11);
        HasSettledTradingDay(saturday, new DateOnly(2026, 7, 14)).Should().BeTrue();
    }

    [Fact]
    public void HasSettledTradingDay_HolidayWeekendWindow_Skips()
    {
        // Independence Day 2026 falls on a Saturday, observed Friday July 3 — a three-day
        // non-trading stretch. With Thursday July 2 stored, cycles up to and including Monday
        // July 6 see only the holiday + weekend in the window and must skip.
        var friday = new DateOnly(2026, 7, 3); // observed holiday
        HasSettledTradingDay(friday, new DateOnly(2026, 7, 6)).Should().BeFalse();
        // Tuesday's cycle sees Monday July 6 settled — fetch resumes.
        HasSettledTradingDay(friday, new DateOnly(2026, 7, 7)).Should().BeTrue();
    }

    [Fact]
    public void HasSettledTradingDay_NeverSyncedStock_Fetches()
    {
        // A stock with no stored prices starts at the 2020 floor — years of settled sessions in
        // the window, must always fetch.
        HasSettledTradingDay(new DateOnly(2020, 1, 1), new DateOnly(2026, 7, 14))
            .Should()
            .BeTrue();
    }
}
