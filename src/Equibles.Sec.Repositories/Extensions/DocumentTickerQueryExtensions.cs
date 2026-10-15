using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories.Extensions;

public static class DocumentTickerQueryExtensions
{
    // MIC:TICKER names one listing outside the US, so its documents are that issuer's; chunks keep the
    // presentation ticker they were cut under, which is why venue scopes filter by document, not ticker.
    public static bool IsVenueSymbol(string symbol) =>
        EquityListingSymbol.TryParse(symbol, out _, out _);

    // A bare ticker keeps its US meaning; MIC:TICKER selects the issuer of that exact active, verified
    // venue listing. Callers resolve aliases and suffixes first and pass the listing's display symbol.
    public static IQueryable<Document> ForListingSymbol(
        this IQueryable<Document> documents,
        string symbol,
        EquiblesFinancialDbContext context
    )
    {
        if (!EquityListingSymbol.TryParse(symbol, out var code, out var ticker))
            return documents.ForUsTicker(symbol, context);
        var issuers = context
            .Set<EquityListing>()
            .Where(listing =>
                listing.Active
                && listing.IdentityState == EquityIdentityState.Verified
                && listing.MarketCountryCode != "US"
                && listing.MarketIdentifierCode == code
                && listing.Ticker == ticker
            )
            .Select(listing => listing.Security.EquityIssuerId);
        return documents.Where(document => issuers.Contains(document.EquityIssuerId));
    }

    // An unqualified symbol selects U.S. issuer claims, including recorded co-registrants.
    public static IQueryable<Document> ForUsTicker(
        this IQueryable<Document> documents,
        string ticker,
        EquiblesFinancialDbContext context
    )
    {
        var symbol = TickerNormalizer.NormalizeListed(ticker);
        if (symbol == null)
            return documents.Where(document => false);
        // Resolve the small issuer set first. A correlated OR on each document can scan the corpus
        // in filing-date order before finding a ticker, exhausting both list and search budgets.
        var presentations = context
            .Set<EquityIssuerPresentation>()
            .Where(presentation =>
                presentation.Listing.MarketCountryCode == "US"
                && presentation.Listing.Ticker == symbol
            )
            .Select(presentation => presentation.EquityIssuerId);
        var listed = context
            .Set<EquityListing>()
            .Where(listing =>
                listing.MarketCountryCode == "US"
                && (listing.IsDirectoryListed || listing.IsReferenceListed)
                && listing.Ticker == symbol
            )
            .Select(listing => listing.Security.EquityIssuerId);
        var issuers = presentations.Union(listed);
        return documents.Where(document => issuers.Contains(document.EquityIssuerId));
    }
}
