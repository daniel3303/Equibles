using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories.Extensions;

public static class DocumentTickerQueryExtensions
{
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
