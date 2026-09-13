namespace Equibles.CommonStocks.Repositories.Models;

public sealed record EquityListingSourceBinding(
    Guid EquityIssuerId,
    Guid EquityListingId,
    string Ticker,
    string MarketIdentifierCode,
    string MarketCountryCode,
    string Isin,
    string Currency,
    decimal QuoteUnitMultiplier,
    string[] SourceMarketIdentifierCodes
);
