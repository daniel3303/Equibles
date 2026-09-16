namespace Equibles.Messaging.Contracts.CommonStocks;

// Raised when website discovery fills an issuer's Website (a previously-empty value). Lets IR
// discovery re-probe the issuer immediately for an investor-relations page off the new website,
// instead of waiting out its own independent cooldown. MarketIdentifierCode is null for a US
// listing and names the venue of a verified listing, so a consumer can tell AIR on Paris from AIR
// on the NYSE without reloading the issuer.
public record StockWebsiteDiscovered(
    Guid CommonStockId,
    string Ticker,
    string Website,
    string MarketIdentifierCode = null
);
