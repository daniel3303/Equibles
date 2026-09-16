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
