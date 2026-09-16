using Equibles.DelayedTrades.BusinessLogic.Prints;
using Equibles.Integrations.DelayedTrades;

namespace Equibles.DelayedTrades.BusinessLogic.Sessions;

// Folds counted prints into per-session bars in one pass; prints arrive in publication order, so first and last
// are decided by trade time, then publication time, then line, never by position in the file.
public static class DelayedTradeSessionAggregator
{
    public const int PricePrecision = 4;

    public static DelayedTradeSessionAggregation Aggregate(
        IEnumerable<DelayedTradePrint> countedPrints,
        TimeZoneInfo zone,
        bool detectDuplicateTradeIds = false
    )
    {
        ArgumentNullException.ThrowIfNull(zone);
        var result = new DelayedTradeSessionAggregation();
        var accumulators =
            new Dictionary<(string Isin, string Venue, DateOnly Date), Accumulator>();
        var seen = detectDuplicateTradeIds ? new HashSet<string>(StringComparer.Ordinal) : null;
        foreach (var print in countedPrints)
        {
            if (seen != null && !string.IsNullOrEmpty(print.TradeId) && !seen.Add(print.TradeId))
            {
                result.DuplicateCount++;
                continue;
            }
            var sessionDate = SessionDate(print.TradedAtUtc, zone);
            var key = (print.Isin, print.Venue, sessionDate);
            if (!accumulators.TryGetValue(key, out var accumulator))
            {
                accumulator = new Accumulator();
                accumulators[key] = accumulator;
            }
            result.PrintCount++;
            accumulator.PrintCount++;
            accumulator.Volume += print.Quantity;
            if (DelayedTradePrintFilter.IsDark(print))
            {
                result.DarkPrintCount++;
                accumulator.DarkPrintCount++;
            }
            if (!DelayedTradePrintFilter.IsPriceForming(print))
                continue;
            result.LitPrintCount++;
            accumulator.LitVolume += print.Quantity;
            if (accumulator.First == null || Precedes(print, accumulator.First))
                accumulator.First = print;
            if (accumulator.Last == null || Precedes(accumulator.Last, print))
                accumulator.Last = print;
            if (accumulator.High == null || print.Price > accumulator.High)
                accumulator.High = print.Price;
            if (accumulator.Low == null || print.Price < accumulator.Low)
                accumulator.Low = print.Price;
        }
        foreach (var (key, accumulator) in accumulators.OrderBy(pair => pair.Key))
        {
            result.Bars.Add(
                new DelayedTradeSessionBar(
                    key.Isin,
                    key.Venue,
                    key.Date,
                    Round(accumulator.First?.Price),
                    Round(accumulator.High),
                    Round(accumulator.Low),
                    Round(accumulator.Last?.Price),
                    Floor(accumulator.Volume),
                    Floor(accumulator.LitVolume),
                    accumulator.PrintCount,
                    accumulator.DarkPrintCount,
                    accumulator.Last
                )
            );
        }
        return result;
    }

    public static DateOnly SessionDate(DateTime tradedAtUtc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(tradedAtUtc, DateTimeKind.Utc),
                zone
            )
        );

    public static decimal? Round(decimal? price) =>
        price == null
            ? null
            : Math.Round(price.Value, PricePrecision, MidpointRounding.AwayFromZero);

    private static long Floor(decimal quantity) =>
        quantity >= long.MaxValue ? long.MaxValue : (long)Math.Floor(quantity);

    private static bool Precedes(DelayedTradePrint left, DelayedTradePrint right)
    {
        var byTrade = left.TradedAtUtc.CompareTo(right.TradedAtUtc);
        if (byTrade != 0)
            return byTrade < 0;
        var byPublication = left.PublishedAtUtc.CompareTo(right.PublishedAtUtc);
        return byPublication != 0 ? byPublication < 0 : left.LineNumber < right.LineNumber;
    }

    private sealed class Accumulator
    {
        public DelayedTradePrint First;
        public DelayedTradePrint Last;
        public decimal? High;
        public decimal? Low;
        public decimal Volume;
        public decimal LitVolume;
        public int PrintCount;
        public int DarkPrintCount;
    }
}
