using System.Linq.Expressions;
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
/// Filer-level SEC documents remain attached to the <see cref="CommonStock"/> row. Market data,
/// congressional trades, and 13F positions retain their exact listed symbol and must be read
/// through <see cref="ResolveListedTicker"/> so sibling securities never bleed together.
/// </para>
/// </summary>
public static class SecondaryTickerPolicy
{
    /// <summary>
    /// Authoritative primary operating-company universe. A primary ticker explicitly present in
    /// the exchange-traded reference feed belongs on the ETF surface and must not consume issuer
    /// financials, shares outstanding, derived short models, or stock rankings.
    /// </summary>
    public static readonly Expression<Func<CommonStock, bool>> PrimaryOperatingCompany = stock =>
        !stock.ReferenceTickers.Contains(stock.Ticker)
        && !stock.ReferenceTickers.Contains(stock.Ticker.Replace(".", "-"));

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
    /// Whether the exact resolved listing is an authoritative exchange-traded product.
    /// ReferenceTickers is populated from the provider's ETF/ETN/ETV/ETS reference feed;
    /// never infer this classification from a ticker or issuer name.
    /// </summary>
    public static bool IsExchangeTradedListing(CommonStock stock, string requestedTicker)
    {
        var requested = TickerNormalizer.NormalizeDashListed(requestedTicker);
        return stock != null
            && requested != null
            && (stock.ReferenceTickers ?? []).Any(reference =>
                string.Equals(
                    TickerNormalizer.NormalizeDashListed(reference),
                    requested,
                    StringComparison.OrdinalIgnoreCase
                )
            );
    }

    /// <summary>
    /// Whether a filer-wide stock read would merge the requested security with a sibling.
    /// Primary operating-company stocks retain their established filer-wide read models;
    /// ETFs and every secondary security require the exact listed ticker.
    /// </summary>
    public static bool RequiresExactListingScope(CommonStock stock, string requestedTicker) =>
        IsSecondarySymbol(stock, requestedTicker)
        || IsExchangeTradedListing(stock, requestedTicker);

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
