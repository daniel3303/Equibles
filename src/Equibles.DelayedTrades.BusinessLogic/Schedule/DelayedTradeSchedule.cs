using Equibles.DelayedTrades.BusinessLogic.Configuration;
using Equibles.EquityMarkets.Data.Catalog;

namespace Equibles.DelayedTrades.BusinessLogic.Schedule;

// Pure: decides from the market's local clock which fetches are due. There is no venue calendar, so a holiday is
// simply a settle attempt that finds an already-marked file, and the file's own prints name the session date.
public static class DelayedTradeSchedule
{
    public static DelayedTradePlan Plan(
        EquityMarket market,
        TimeZoneInfo zone,
        DateTime utcNow,
        DelayedTradeScraperOptions options,
        DelayedTradeMarketState state
    )
    {
        var local = DelayedTradeClock.Local(utcNow, zone);
        var time = TimeOnly.FromDateTime(local);
        var date = DateOnly.FromDateTime(local);
        var weekday = IsWeekday(local.DayOfWeek);

        var intradayStart = market.SessionOpen.AddMinutes(-options.PreOpenMinutes);
        var intradayEnd = market.ClosingAuctionEnd.AddMinutes(options.PostAuctionMinutes);
        var intraday =
            weekday
            && time >= intradayStart
            && time <= intradayEnd
            && IsDue(state.LastIntradayPollUtc, utcNow, options.IntradayPollMinutes);

        var target = SettleTarget(local, options);
        var settle =
            (state.SettledThroughDate == null || state.SettledThroughDate < target)
            && IsDue(state.LastSettleAttemptUtc, utcNow, options.SettlePollMinutes);

        var recheck =
            options.RecheckMinutesAfterOpen > 0
            && weekday
            && time >= market.SessionOpen.AddMinutes(options.RecheckMinutesAfterOpen)
            && state.LastRecheckDate != date;

        return new DelayedTradePlan(intraday, settle, recheck);
    }

    // The last weekday whose file the venue is expected to have flipped to: yesterday's session from the settle
    // window start, the one before it until then. Weekends and holidays resolve to an already-settled date.
    public static DateOnly SettleTarget(DateTime local, DelayedTradeScraperOptions options)
    {
        var date = DateOnly.FromDateTime(local);
        var candidate =
            TimeOnly.FromDateTime(local) >= options.SettleWindowStart
                ? date.AddDays(-1)
                : date.AddDays(-2);
        while (!IsWeekday(candidate.DayOfWeek))
            candidate = candidate.AddDays(-1);
        return candidate;
    }

    public static bool IsWeekday(DayOfWeek day) =>
        day != DayOfWeek.Saturday && day != DayOfWeek.Sunday;

    private static bool IsDue(DateTime? lastAttemptUtc, DateTime utcNow, int intervalMinutes) =>
        lastAttemptUtc == null
        || lastAttemptUtc <= utcNow - TimeSpan.FromMinutes(Math.Max(1, intervalMinutes));
}
