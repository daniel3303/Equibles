using Equibles.CommonStocks.Data.Helpers;

namespace Equibles.CommonStocks.BusinessLogic.Websites;

/// <summary>
/// The identifying slice of an issuer handed to <see cref="IWebsiteSource"/> implementations,
/// enough for any backend to key its lookup (documents by issuer id, Wikidata by CIK or LEI,
/// Yahoo by the venue-qualified symbol) without passing tracked entities between service scopes.
/// </summary>
public sealed record WebsiteSourceStock(
    Guid Id,
    string Ticker,
    string Cik,
    string LegalEntityIdentifier = null,
    string Isin = null,
    string MarketIdentifierCode = null,
    string MarketCountryCode = "US"
)
{
    public string Symbol =>
        EquityListingSymbol.Display(Ticker, MarketCountryCode, MarketIdentifierCode);
}
