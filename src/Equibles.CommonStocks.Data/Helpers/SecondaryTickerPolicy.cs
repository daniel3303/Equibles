using Equibles.CommonStocks.Data.Models;

namespace Equibles.CommonStocks.Data.Helpers;

/// <summary>
/// One <see cref="CommonStock"/> row is one SEC FILER, and the filer's other listed
/// symbols ride along in <see cref="CommonStock.SecondaryTickers"/>: share classes
/// (BRK-A beside BRK-B, GOOG beside GOOGL), warrants, units, preferreds, and separate
/// fund series of one trust (BWET beside BDRY). Those are DIFFERENT securities and they
/// trade at their own prices.
/// <para>
/// Price data stores the exact listed ticker on every bar, including the filer's current
/// primary, and keeps a separate series for each authoritative secondary ticker. A caller
/// therefore resolves the requested spelling before reading bars; BRK-A can never fall
/// through to BRK-B.
/// </para>
/// <para>
/// Surfaces reading FILER-level data (filings, 13F holdings, insider transactions) remain
/// attached to the <see cref="CommonStock"/> row. Price surfaces use
/// <see cref="ResolveListedTicker"/> so their identity is the traded symbol instead.
/// </para>
/// </summary>
public static class SecondaryTickerPolicy
{
    /// <summary>
    /// Resolves a caller's spelling to the exact canonical ticker carried by the filer.
    /// The dot class-share notation is mechanically folded to the stored dash form
    /// (BRK.B -&gt; BRK-B). Returns null when the ticker is not one of the filer's listings.
    /// </summary>
    public static string ResolveListedTicker(CommonStock stock, string requestedTicker)
    {
        if (stock?.Ticker == null || string.IsNullOrWhiteSpace(requestedTicker))
            return null;

        var requested = TickerNormalizer.Normalize(requestedTicker);
        if (requested == null)
            return null;

        var candidates = requested.Contains('.')
            ? new[] { requested, requested.Replace('.', '-') }
            : new[] { requested };

        foreach (var candidate in candidates)
        {
            if (string.Equals(stock.Ticker, candidate, StringComparison.Ordinal))
                return stock.Ticker;

            var secondary = (stock.SecondaryTickers ?? []).FirstOrDefault(ticker =>
                string.Equals(ticker, candidate, StringComparison.OrdinalIgnoreCase)
            );
            if (secondary != null)
                return secondary;
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="requestedTicker"/> named a symbol other than
    /// <paramref name="stock"/>'s primary one. Accepts the dot class-share notation for
    /// the dash form the data stores (BRK.B is BRK-B, not a secondary symbol) so the
    /// spelling a caller happens to use cannot change the answer.
    /// </summary>
    public static bool IsSecondarySymbol(CommonStock stock, string requestedTicker)
    {
        var resolved = ResolveListedTicker(stock, requestedTicker);
        return resolved != null && !string.Equals(resolved, stock.Ticker, StringComparison.Ordinal);
    }

    /// <summary>
    /// Legacy refusal text retained for binary-compatible callers. Secondary listings now have
    /// independent price series, so callers should resolve the symbol and query that series.
    /// </summary>
    [Obsolete("Secondary listings have independent price series; use ResolveListedTicker.")]
    public static string NoPriceSeriesMessage(CommonStock stock, string requestedTicker)
    {
        var resolved = ResolveListedTicker(stock, requestedTicker) ?? requestedTicker;
        return $"No price data found for '{resolved}'. It is listed separately from "
            + $"{stock.Ticker} ({stock.Name}) and its price history may still be backfilling.";
    }
}
