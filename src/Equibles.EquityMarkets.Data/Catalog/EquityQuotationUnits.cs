namespace Equibles.EquityMarkets.Data.Catalog;

// Source currency tokens map to the stored ISO code plus the multiplier that turns a quote into major units;
// a token outside this table leaves the listing unverified rather than guessing a scale.
public static class EquityQuotationUnits
{
    private static readonly IReadOnlyDictionary<
        string,
        (string Currency, decimal Multiplier)
    > Units = new Dictionary<string, (string, decimal)>(StringComparer.Ordinal)
    {
        ["EUR"] = ("EUR", 1m),
        ["NOK"] = ("NOK", 1m),
        ["SEK"] = ("SEK", 1m),
        ["DKK"] = ("DKK", 1m),
        ["PLN"] = ("PLN", 1m),
        ["CHF"] = ("CHF", 1m),
        ["USD"] = ("USD", 1m),
        ["GBP"] = ("GBP", 1m),
        ["GBp"] = ("GBP", 0.01m),
        ["GBX"] = ("GBP", 0.01m),
    };

    public static bool TryResolve(string token, out string currency, out decimal multiplier)
    {
        if (token != null && Units.TryGetValue(token, out var unit))
        {
            (currency, multiplier) = unit;
            return true;
        }
        currency = null;
        multiplier = 0m;
        return false;
    }

    public static bool Matches(string token, string currency, decimal? multiplier) =>
        TryResolve(token, out var resolvedCurrency, out var resolvedMultiplier)
        && resolvedCurrency == currency
        && resolvedMultiplier == multiplier;
}
