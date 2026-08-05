namespace Equibles.Core.Contracts;

/// <summary>
/// Provides stock closing prices by (CommonStockId, ListedTicker, Date).
/// If the exact date is not a trading day, implementations should return
/// the closest prior trading day's price.
/// </summary>
public interface IStockPriceProvider
{
    /// <summary>
    /// Batch-fetches closing prices for the requested (stock, listing, date) triples.
    /// Returns only triples where a price was found, keyed exactly as requested.
    /// <para>
    /// A null <c>ListedTicker</c> means the filer's current PRIMARY listing. A non-null
    /// value names one of the filer's other listed securities (a sibling share class,
    /// unit, or fund series) and must be priced from that exact series — sibling classes
    /// trade at their own prices (BRK-A ≈ 1500× BRK-B), so substituting the primary's
    /// close is never acceptable.
    /// </para>
    /// </summary>
    Task<
        Dictionary<(Guid CommonStockId, string ListedTicker, DateOnly Date), decimal>
    > GetClosingPrices(
        IEnumerable<(Guid CommonStockId, string ListedTicker, DateOnly Date)> requests,
        CancellationToken cancellationToken = default
    );
}
