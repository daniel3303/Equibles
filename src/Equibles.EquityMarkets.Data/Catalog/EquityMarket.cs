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
    string DelayedTradeLocationCode,
    IReadOnlyList<string> FirdsVenueCodes = null,
    IReadOnlyList<string> HomeVenueCodes = null,
    string FirdsAuthority = "ESMA"
)
{
    // The venue codes FIRDS records this market's lines under; a regulator files Xetra by segment, never as XETR.
    public IReadOnlyList<string> FirdsVenueCodes { get; init; } =
        FirdsVenueCodes ?? MarketIdentifierCodes;

    // Every venue code, operating or segment, that places a share's home market here.
    public IReadOnlyList<string> HomeVenueCodes { get; init; } =
        HomeVenueCodes ?? MarketIdentifierCodes;

    public bool Contains(string marketIdentifierCode) =>
        marketIdentifierCode != null
        && MarketIdentifierCodes.Contains(marketIdentifierCode, StringComparer.Ordinal);

    public bool IsFirdsVenue(string marketIdentifierCode) =>
        marketIdentifierCode != null
        && FirdsVenueCodes.Contains(marketIdentifierCode, StringComparer.Ordinal);

    public bool IsHomeVenue(string marketIdentifierCode) =>
        marketIdentifierCode != null
        && HomeVenueCodes.Contains(marketIdentifierCode, StringComparer.Ordinal);
}
