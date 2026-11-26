using System.Globalization;

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

    // The operating code people write for a market whose lines are stored under segment codes only (Milan's XMIL);
    // it names the market for a reader and is never stored on a listing.
    public string OperatingCode { get; init; }

    // The suffix people write after a ticker on a market that states no provider symbol (B3's ".SA"); it names
    // the market for a reader and never starts a price series.
    public string CallerSuffix { get; init; }

    // The zone whose clock fixes the close when it is not the market's own (B3 closes on New York's clock);
    // SessionClose and ClosingAuctionEnd then state the market's clock while both zones keep standard time.
    public string CloseTimeZoneId { get; init; }

    // The last continuous print and the closing-auction end on the market's own clock for one trading day.
    public (TimeOnly SessionClose, TimeOnly ClosingAuctionEnd) CloseOn(DateOnly day)
    {
        if (CloseTimeZoneId == null)
            return (SessionClose, ClosingAuctionEnd);
        var shift = DaylightShift(CloseTimeZoneId, day) - DaylightShift(TimeZoneId, day);
        return (SessionClose.Add(-shift), ClosingAuctionEnd.Add(-shift));
    }

    private static TimeSpan DaylightShift(string zoneId, DateOnly day)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        return zone.GetUtcOffset(day.ToDateTime(new TimeOnly(12, 0))) - zone.BaseUtcOffset;
    }

    // The stored codes of the lines that price the market's shares, in catalog order.
    public IEnumerable<string> RegularLineCodes =>
        MarketIdentifierCodes.Where(code => !IsExtendedSession(code));

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

    // The Yahoo-style symbol for one of this market's tickers, which price and reference providers share;
    // null without a suffix or when a numeric code is not the venue's width.
    public string YahooSymbol(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker) || string.IsNullOrWhiteSpace(YahooSuffix))
            return null;
        if (NumericTickerWidth is { } sourceWidth && ticker.Length != sourceWidth)
            return null;
        if (YahooNumericSymbolWidth is not { } width)
            return ticker + YahooSuffix;
        // Exchange numeric codes and provider symbols can have different leading-zero widths.
        return
            ticker.All(char.IsAsciiDigit)
            && int.TryParse(ticker, NumberStyles.None, CultureInfo.InvariantCulture, out var code)
            && code > 0
            ? code.ToString(
                "D" + width.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture
            ) + YahooSuffix
            : null;
    }
}
