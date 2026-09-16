using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.DelayedTrades.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.DelayedTrades.Repositories;

public class LatestDelayedTradeRepository(EquiblesFinancialDbContext dbContext)
    : BaseRepository<LatestDelayedTrade>(dbContext)
{
    public Task<LatestDelayedTrade> GetByListing(
        Guid listingId,
        CancellationToken cancellationToken = default
    ) => GetAll().SingleOrDefaultAsync(row => row.EquityListingId == listingId, cancellationToken);

    public IQueryable<LatestDelayedTrade> GetByMarket(string marketCode) =>
        GetAll().Where(row => row.MarketCode == marketCode);

    // Every active verified listing on the given venues, with the fields the print match and the bar write check.
    public IQueryable<DelayedTradeListingReference> GetVerifiedListings(IEnumerable<string> mics)
    {
        var codes = mics.ToList();
        return DbContext
            .Set<EquityListing>()
            .Where(listing =>
                listing.Active
                && listing.IdentityState == EquityIdentityState.Verified
                && listing.Security.Isin != null
                && codes.Contains(listing.MarketIdentifierCode)
            )
            .Select(listing => new DelayedTradeListingReference(
                listing.Id,
                listing.Security.EquityIssuerId,
                listing.Security.Isin,
                listing.MarketIdentifierCode,
                listing.Ticker,
                listing.TradingCurrency,
                listing.QuoteUnitMultiplier,
                listing.IdentityState,
                listing.Active
            ));
    }
}
