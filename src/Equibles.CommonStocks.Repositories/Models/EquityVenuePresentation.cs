namespace Equibles.CommonStocks.Repositories.Models;

/// <summary>
/// The venue listing a company shows the public in place of its US presentation line, which no US exchange lists.
/// </summary>
public sealed record EquityVenuePresentation(
    Guid IssuerId,
    Guid ListingId,
    string MarketIdentifierCode,
    string MarketCountryCode,
    string Ticker,
    string UsTicker
);
