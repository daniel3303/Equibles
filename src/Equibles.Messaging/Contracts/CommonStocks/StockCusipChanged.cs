namespace Equibles.Messaging.Contracts.CommonStocks;

// Raised when a CommonStock's CUSIP is set or changed (typically the FTD
// scraper seeding a previously-null CUSIP). Lets the Holdings module
// invalidate any quarterly 13F data sets that were marked processed before
// this stock was resolvable, so they get re-imported and backfilled.
public record StockCusipChanged(
    Guid CommonStockId,
    string Ticker,
    string PreviousCusip,
    string Cusip
);
