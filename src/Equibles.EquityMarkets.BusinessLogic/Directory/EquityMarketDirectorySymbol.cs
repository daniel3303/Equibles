using Equibles.CommonStocks.Data.Helpers;

namespace Equibles.EquityMarkets.BusinessLogic.Directory;

// A venue writes a share class after its own separator (a space on Nasdaq Nordic, a dot on BME and London) and
// London pads a short mnemonic with a trailing dot; the stored ticker writes the separator as a hyphen, which is
// the spelling the site route, the display symbol and the price provider's symbol share.
public static class EquityMarketDirectorySymbol
{
    public static string Normalize(string venueSymbol)
    {
        if (string.IsNullOrWhiteSpace(venueSymbol))
            return null;
        var tokens = venueSymbol
            .Trim()
            .TrimEnd('.')
            .Split([' ', '.', '-'], StringSplitOptions.None);
        if (tokens.Any(token => token.Length == 0))
            return null;
        return TickerNormalizer.NormalizeListed(string.Join('-', tokens));
    }
}
