using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories.Models;
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

    public IQueryable<EquityListing> GetByRecordedUsSymbol(Guid issuerId, string ticker) =>
        GetAll()
            .Where(listing =>
                listing.MarketCountryCode == "US"
                && listing.Security.EquityIssuerId == issuerId
                && (
                    listing.Ticker == ticker
                    || listing.TickerAliases.Any(alias => alias.Ticker == ticker)
                )
            );

    public IQueryable<EquityListingSymbolReference> GetRecordedUsSymbols(
        IEnumerable<Guid> issuerIds
    )
    {
        var listings = GetAll()
            .Where(listing =>
                listing.MarketCountryCode == "US"
                && issuerIds.Contains(listing.Security.EquityIssuerId)
            );
        return listings
            .Select(listing => new EquityListingSymbolReference
            {
                EquityIssuerId = listing.Security.EquityIssuerId,
                EquityListingId = listing.Id,
                Ticker = listing.Ticker,
            })
            .Union(
                listings
                    .SelectMany(listing => listing.TickerAliases)
                    .Select(alias => new EquityListingSymbolReference
                    {
                        EquityIssuerId = alias.Listing.Security.EquityIssuerId,
                        EquityListingId = alias.EquityListingId,
                        Ticker = alias.Ticker,
                    })
            );
    }

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
