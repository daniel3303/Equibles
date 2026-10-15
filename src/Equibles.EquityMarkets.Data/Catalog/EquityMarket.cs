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
    string FirdsAuthority = "ESMA",
    int? YahooNumericSymbolWidth = null,
    int? NumericTickerWidth = null,
    IReadOnlyList<string> ExtendedSessionCodes = null,
    bool HomeRequiresIssuerRequest = false
)
{
    // Venue codes that quote the market's own shares again in another session (Milan's after-hours MTAH);
    // such a line never prices the share, so it loses the presentation to its share's regular line.
    public IReadOnlyList<string> ExtendedSessionCodes { get; init; } = ExtendedSessionCodes ?? [];

    // The venue codes FIRDS records this market's lines under; a regulator files Xetra by segment, never as XETR.
    public IReadOnlyList<string> FirdsVenueCodes { get; init; } =
        FirdsVenueCodes ?? MarketIdentifierCodes;

    // Set where a home venue's open market includes foreign shares without the issuer's consent (German Freiverkehr);
    // a FIRDS home share then needs a home-venue line that states the issuer requested admission.
    public bool HomeRequiresIssuerRequest { get; init; } = HomeRequiresIssuerRequest;

    // Every venue code, operating or segment, that places a share's home market here.
    public IReadOnlyList<string> HomeVenueCodes { get; init; } =
        HomeVenueCodes ?? MarketIdentifierCodes;

    public bool Contains(string marketIdentifierCode) =>
        marketIdentifierCode != null
        && MarketIdentifierCodes.Contains(marketIdentifierCode, StringComparer.Ordinal);

    public bool IsFirdsVenue(string marketIdentifierCode) =>
        marketIdentifierCode != null
        && FirdsVenueCodes.Contains(marketIdentifierCode, StringComparer.Ordinal);

    public bool IsExtendedSession(string marketIdentifierCode) =>
        marketIdentifierCode != null
        && ExtendedSessionCodes.Contains(marketIdentifierCode, StringComparer.Ordinal);

    public bool IsHomeVenue(string marketIdentifierCode) =>
        marketIdentifierCode != null
        && HomeVenueCodes.Contains(marketIdentifierCode, StringComparer.Ordinal);
}
