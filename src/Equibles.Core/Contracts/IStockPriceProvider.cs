namespace Equibles.Core.Contracts;

/// <summary>
/// Provides stock closing prices by (CommonStockId, Date).
/// If the exact date is not a trading day, implementations should return
/// the closest prior trading day's price.
/// </summary>
public interface IStockPriceProvider {
    /// <summary>
    /// Batch-fetches closing prices for the requested (stock, date) pairs.
    /// Returns only pairs where a price was found.
    /// </summary>
    Task<Dictionary<(Guid CommonStockId, DateOnly Date), decimal>> GetClosingPrices(
        IEnumerable<(Guid CommonStockId, DateOnly Date)> requests,
        CancellationToken cancellationToken = default);
}
