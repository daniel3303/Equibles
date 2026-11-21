using Equibles.CommonStocks.Data.Models;

namespace Equibles.Yahoo.Mcp.Captures;

// The exact series a price answer read: a US filer's listed symbol (Venue null) or one venue listing. Name is
// null for a secondary US symbol, since the filer's name describes its primary listing only, and QuoteUnit is
// null for a US listing, which is quoted in USD.
public sealed record StockPriceCaptureListing(
    EquityIssuer Stock,
    string PriceTicker,
    EquityListing Venue,
    string Symbol,
    string Name,
    string QuoteUnit
);
