using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Finra.Data.Models;

namespace Equibles.Finra.Repositories;

public class DailyShortVolumeRepository : BaseRepository<DailyShortVolume>
{
    public DailyShortVolumeRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<DailyShortVolume> GetByListingId(Guid listingId, DateOnly date) =>
        GetHistoryByListingId(listingId).Where(row => row.Date == date);

    public IQueryable<DailyShortVolume> GetHistoryByListingId(Guid listingId) =>
        GetAll().Where(row => row.EquityListingId == listingId);

    public IQueryable<DailyShortVolume> GetByStock(EquityIssuer stock, DateOnly date) =>
        GetHistoryByStock(stock).Where(row => row.Date == date);

    public IQueryable<DailyShortVolume> GetByListing(
        EquityIssuer stock,
        string listedTicker,
        DateOnly date
    ) => GetHistoryByListing(stock, listedTicker).Where(row => row.Date == date);

    public IQueryable<DailyShortVolume> GetHistoryByStock(EquityIssuer stock) =>
        GetAll()
            .Where(row =>
                row.Listing.Security.EquityIssuerId == stock.Id
                && row.EquityListingId == row.Listing.Security.Issuer.Presentation.EquityListingId
            );

    public virtual IQueryable<DailyShortVolume> GetHistoryByListing(
        EquityIssuer stock,
        string listedTicker
    )
    {
        var listingIds = DbContext
            .Set<EquityListing>()
            .Where(row =>
                row.Security.EquityIssuerId == stock.Id
                && row.MarketCountryCode == "US"
                && row.Ticker == listedTicker
            )
            .Select(row => row.Id);
        return GetAll()
            .Where(row => listingIds.Count() == 1 && listingIds.Contains(row.EquityListingId));
    }

    public IQueryable<DateOnly> GetLatestDate()
    {
        return GetAll().LatestValue(d => d.Date, distinct: true);
    }

    public IQueryable<DateOnly> GetEarliestDate()
    {
        return GetAll().Select(d => d.Date).Distinct().OrderBy(d => d).Take(1);
    }

    public IQueryable<DailyShortVolume> GetByDate(DateOnly date)
    {
        return GetAll().Where(d => d.Date == date);
    }
}
