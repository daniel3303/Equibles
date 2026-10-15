using Equibles.CommonStocks.Data.Models;

namespace Equibles.CommonStocks.Data.Helpers;

/// <summary>
/// The one spelling of a listing for people, logs and file names. A US listing is its bare
/// ticker; a venue listing is qualified by its market identifier code, so AIR on Euronext Paris
/// never reads as AIR on the NYSE.
/// </summary>
public static class EquityListingSymbol
{
    public const char DisplaySeparator = ':';
    public const char FileSafeSeparator = '-';

    public static string Display(EquityIssuer issuer) => Display(issuer?.Presentation?.Listing);

    public static string Display(EquityListing listing) =>
        listing == null
            ? null
            : Display(listing.Ticker, listing.MarketCountryCode, listing.MarketIdentifierCode);

    public static string Display(
        string ticker,
        string marketCountryCode,
        string marketIdentifierCode
    ) => Compose(ticker, marketCountryCode, marketIdentifierCode, DisplaySeparator);

    public static string FileSafe(EquityIssuer issuer) => FileSafe(issuer?.Presentation?.Listing);

    public static string FileSafe(EquityListing listing) =>
        listing == null
            ? null
            : FileSafe(listing.Ticker, listing.MarketCountryCode, listing.MarketIdentifierCode);

    public static string FileSafe(
        string ticker,
        string marketCountryCode,
        string marketIdentifierCode
    ) => Compose(ticker, marketCountryCode, marketIdentifierCode, FileSafeSeparator);

    /// <summary>
    /// Reads the display spelling back: <c>XPAR:AIR</c> yields the venue code and ticker. A bare
    /// ticker is not a venue symbol, because a US listing is always written without a code.
    /// </summary>
    public static bool TryParse(string symbol, out string marketIdentifierCode, out string ticker)
    {
        marketIdentifierCode = null;
        ticker = null;
        if (string.IsNullOrWhiteSpace(symbol))
            return false;

        var trimmed = symbol.Trim();
        var separator = trimmed.IndexOf(DisplaySeparator);
        if (separator != MarketIdentifierCodeLength)
            return false;
        var code = trimmed[..separator].ToUpperInvariant();
        var listed = TickerNormalizer.NormalizeListed(trimmed[(separator + 1)..]);
        if (!code.All(char.IsAsciiLetterOrDigit) || listed == null)
            return false;

        marketIdentifierCode = code;
        ticker = listed;
        return true;
    }

    private const int MarketIdentifierCodeLength = 4;

    /// <summary>
    /// What a reader whose data covers US listings only answers an exchange-qualified symbol. It never
    /// suggests the bare ticker, which often names a different US company (XPAR:AIR is Airbus, AIR is AAR).
    /// </summary>
    public static string UsOnlyNotice(string symbol) =>
        $"'{symbol?.Trim()}' names a listing outside the US (MIC:TICKER), and this data covers US listings only. "
        + "Its bare ticker can belong to a different US company, so use a US ticker only when it is this company's own US listing.";

    private static string Compose(
        string ticker,
        string marketCountryCode,
        string marketIdentifierCode,
        char separator
    )
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return null;
        // US listings store no venue code, and a venue listing missing one falls back to the bare ticker.
        if (marketCountryCode == "US" || string.IsNullOrWhiteSpace(marketIdentifierCode))
            return ticker;
        return marketIdentifierCode + separator + ticker;
    }
}
