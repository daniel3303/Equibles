using Equibles.CommonStocks.Data.Models;
using Equibles.Data;

namespace Equibles.CommonStocks.Repositories;

public class EquityListingRepository : BaseRepository<EquityListing>
{
    public EquityListingRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<EquityListing> GetByLegacyKey(Guid stockId, string ticker) =>
        DbContext
            .Set<LegacyEquityListing>()
            .Where(row => row.CommonStockId == stockId && row.ListedTicker == ticker)
            .Select(row => row.Listing);

    public IQueryable<LegacyEquityListing> GetLegacyMappings(IEnumerable<Guid> issuerIds) =>
        DbContext.Set<LegacyEquityListing>().Where(row => issuerIds.Contains(row.CommonStockId));

    public IQueryable<EquityListing> GetVerifiedByMarket(string mic, string ticker) =>
        GetAll()
            .Where(row =>
                row.Active
                && row.IdentityState == EquityIdentityState.Verified
                && row.MarketIdentifierCode == mic
                && row.Ticker == ticker
            );

    public IQueryable<EquityListing> GetActiveUsListings() =>
        GetAll().Where(listing => listing.Active && listing.MarketCountryCode == "US");

    public IQueryable<EquityListing> GetUsByTicker(string ticker) =>
        GetActiveUsListings().Where(listing => listing.Ticker == ticker);
}
