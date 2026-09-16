using System.Collections.Concurrent;
using Equibles.EquityMarkets.Data.Catalog;

namespace Equibles.DelayedTrades.BusinessLogic.Schedule;

public static class DelayedTradeClock
{
    private static readonly ConcurrentDictionary<string, TimeZoneInfo> Zones = new(
        StringComparer.Ordinal
    );

    public static TimeZoneInfo Zone(EquityMarket market) =>
        Zones.GetOrAdd(market.TimeZoneId, TimeZoneInfo.FindSystemTimeZoneById);

    public static DateTime Local(DateTime utcNow, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone);

    public static DateOnly LocalDate(DateTime utcNow, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(Local(utcNow, zone));
}
