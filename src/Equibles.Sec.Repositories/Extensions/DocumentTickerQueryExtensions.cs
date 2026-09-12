using Equibles.CommonStocks.Data.Helpers;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories.Extensions;

public static class DocumentTickerQueryExtensions
{
    // An unqualified symbol selects U.S. issuer claims, including recorded co-registrants.
    public static IQueryable<Document> ForUsTicker(
        this IQueryable<Document> documents,
        string ticker
    )
    {
        var symbol = TickerNormalizer.NormalizeListed(ticker);
        if (symbol == null)
            return documents.Where(document => false);
        return documents.Where(document =>
            (
                document.Issuer.Presentation != null
                && document.Issuer.Presentation.Listing.MarketCountryCode == "US"
                && document.Issuer.Presentation.Listing.Ticker == symbol
            )
            || document.Issuer.Securities.Any(security =>
                security.Listings.Any(listing =>
                    listing.MarketCountryCode == "US"
                    && (listing.IsDirectoryListed || listing.IsReferenceListed)
                    && listing.Ticker == symbol
                )
            )
        );
    }
}
