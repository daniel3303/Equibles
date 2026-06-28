namespace Equibles.Messaging.Contracts.CommonStocks;

// Raised when website discovery fills a CommonStock's Website (a previously-empty value). Lets IR
// discovery re-probe the stock immediately for an investor-relations page off the new website,
// instead of waiting out its own independent cooldown — the website is the input IR discovery needs,
// so a new website should cascade straight into an IR probe.
public record StockWebsiteDiscovered(Guid CommonStockId, string Ticker, string Website);
