using Equibles.Yahoo.Data.Models;

namespace Equibles.Yahoo.Data.Prices;

// Ownership of a stored daily bar is carried by its SourceTicker: a venue-derived bar is keyed
// `MIC:ISIN`, and no listed ticker can contain the separator, so the two writers never collide.
public static class VenuePriceSource
{
    public const char Separator = ':';

    public static string Key(string marketIdentifierCode, string isin)
    {
        if (string.IsNullOrWhiteSpace(marketIdentifierCode) || string.IsNullOrWhiteSpace(isin))
            throw new ArgumentException("A venue price key needs both a MIC and an ISIN.");
        return string.Concat(
            marketIdentifierCode.Trim().ToUpperInvariant(),
            Separator,
            isin.Trim().ToUpperInvariant()
        );
    }

    public static bool IsVenueKey(string sourceTicker) =>
        sourceTicker != null && sourceTicker.Contains(Separator);

    public static bool IsVenueOwned(EquityDailyStockPrice price) => IsVenueKey(price.SourceTicker);

    // Null-safe on purpose: a SQL `NOT (NULL LIKE ...)` is NULL and drops the row, and the
    // in-memory provider throws on a null receiver.
    public static IQueryable<EquityDailyStockPrice> YahooOwned(
        this IQueryable<EquityDailyStockPrice> prices
    ) => prices.Where(price => price.SourceTicker == null || !price.SourceTicker.Contains(":"));

    public static IQueryable<EquityDailyStockPrice> VenueOwned(
        this IQueryable<EquityDailyStockPrice> prices
    ) => prices.Where(price => price.SourceTicker != null && price.SourceTicker.Contains(":"));
}
