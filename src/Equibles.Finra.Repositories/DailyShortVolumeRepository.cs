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

    public IQueryable<DailyShortVolume> GetByStock(CommonStock stock, DateOnly date) =>
        GetHistoryByStock(stock).Where(row => row.Date == date);

    public IQueryable<DailyShortVolume> GetByListing(
        CommonStock stock,
        string listedTicker,
        DateOnly date
    ) => GetHistoryByListing(stock, listedTicker).Where(row => row.Date == date);

    public IQueryable<DailyShortVolume> GetHistoryByStock(CommonStock stock) =>
        GetAll()
            .Where(row =>
                row.Listing.Security.EquityIssuerId == stock.Id
                && row.EquityListingId == row.Listing.Security.Issuer.Presentation.EquityListingId
            );

    public virtual IQueryable<DailyShortVolume> GetHistoryByListing(
        CommonStock stock,
        string listedTicker
    )
    {
        var listingIds = DbContext
            .Set<LegacyEquityListing>()
            .Where(row => row.CommonStockId == stock.Id && row.ListedTicker == listedTicker)
            .Select(row => row.EquityListingId);
        return GetAll().Where(row => listingIds.Contains(row.EquityListingId));
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
