namespace Equibles.CommonStocks.Data.Helpers;

public static class TickerNormalizer
{
    // Canonical ticker form for case-insensitive lookups. Upper-cases with the invariant
    // culture so a host's locale (e.g. Turkish dotless-i) can't fork the mapping.
    public static string Normalize(string ticker) => ticker.Trim().ToUpperInvariant();
}
