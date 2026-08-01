using Equibles.CommonStocks.Data.Models;

namespace Equibles.CommonStocks.Data.Helpers;

/// <summary>
/// One <see cref="CommonStock"/> row is one SEC FILER, and the filer's other listed
/// symbols ride along in <see cref="CommonStock.SecondaryTickers"/>: share classes
/// (BRK-A beside BRK-B, GOOG beside GOOGL), warrants, units, preferreds, and separate
/// fund series of one trust (BWET beside BDRY). Those are DIFFERENT securities and they
/// trade at their own prices.
/// <para>
/// Price data is stored once per row and fetched under the PRIMARY symbol only, so a
/// secondary symbol has no series of its own — and a lookup that accepts either
/// spelling answers it with the primary's bars. That reported BRK-A at BRK-B's close,
/// off by the 1500:1 the charter fixes between the two classes.
/// </para>
/// <para>
/// Price surfaces therefore resolve through <see cref="IsSecondarySymbol"/> and decline
/// rather than substitute. Surfaces reading FILER-level data (filings, 13F holdings,
/// insider transactions) are unaffected: those genuinely belong to the whole company,
/// which is why the permissive lookup exists.
/// </para>
/// </summary>
public static class SecondaryTickerPolicy
{
    /// <summary>
    /// Whether <paramref name="requestedTicker"/> named a symbol other than
    /// <paramref name="stock"/>'s primary one. Accepts the dot class-share notation for
    /// the dash form the data stores (BRK.B is BRK-B, not a secondary symbol) so the
    /// spelling a caller happens to use cannot change the answer.
    /// </summary>
    public static bool IsSecondarySymbol(CommonStock stock, string requestedTicker)
    {
        if (stock?.Ticker == null || requestedTicker == null)
            return false;

        var requested = TickerNormalizer.Normalize(requestedTicker);
        var primary = TickerNormalizer.Normalize(stock.Ticker);
        if (requested == primary)
            return false;

        return !requested.Contains('.') || requested.Replace('.', '-') != primary;
    }

    /// <summary>
    /// Why the requested symbol has no prices, naming the primary so the caller can
    /// retry. Says which company owns the series rather than only that the symbol
    /// failed — a bare "not found" reads as missing coverage.
    /// </summary>
    public static string NoPriceSeriesMessage(CommonStock stock, string requestedTicker)
    {
        return $"No price series for '{requestedTicker}'. It is a secondary symbol on "
            + $"{stock.Ticker} ({stock.Name}) — a different security from the same SEC filer "
            + "(share class, warrant, unit, preferred or fund series). Prices are stored per "
            + $"filer under the primary symbol, so request {stock.Ticker} for that company's "
            + "own prices.";
    }
}
