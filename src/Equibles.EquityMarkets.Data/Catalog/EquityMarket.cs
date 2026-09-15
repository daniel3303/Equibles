namespace Equibles.EquityMarkets.Data.Catalog;

// One venue family the directory and price lanes know how to serve; the code is the join key on every stored row.
public sealed record EquityMarket(
    string Code,
    string Name,
    string CountryCode,
    IReadOnlyList<string> MarketIdentifierCodes,
    string Currency,
    string YahooSuffix,
    string YahooExchangeCode,
    string TimeZoneId,
    TimeOnly SessionOpen,
    TimeOnly SessionClose,
    TimeOnly ClosingAuctionEnd,
    string DirectorySource,
    string DelayedTradeSource,
    string DelayedTradeLocationCode
)
{
    public bool Contains(string marketIdentifierCode) =>
        marketIdentifierCode != null
        && MarketIdentifierCodes.Contains(marketIdentifierCode, StringComparer.Ordinal);
}
