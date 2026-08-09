namespace Equibles.Messaging.Contracts.CommonStocks;

// Raised when an operator attaches an additional CIK (a predecessor registrant
// after a holdco reorganisation, or a co-registrant subsidiary) to a stock.
// Filing discovery and the document scraper re-enumerate every CIK each sweep,
// so documents backfill on their own — but the financial-facts lane checkpoints
// on the newest filed date it has seen, and a predecessor's facts are all OLDER
// than that watermark. The consumer resets the stock's facts checkpoint so the
// next cycle re-imports the full (now multi-CIK) companyfacts history.
public record StockSecondaryCikAttached(Guid CommonStockId, string Ticker, string Cik);
