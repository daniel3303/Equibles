using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Finra.Data.Models;

namespace Equibles.Finra.Repositories;

public class OffExchangeVolumeRepository : BaseRepository<OffExchangeVolume>
{
    public OffExchangeVolumeRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<OffExchangeVolume> GetByListingId(Guid listingId, DateOnly date) =>
        GetHistoryByListingId(listingId).Where(row => row.WeekStartDate == date);

    public IQueryable<OffExchangeVolume> GetHistoryByListingId(Guid listingId) =>
        GetAll().Where(row => row.EquityListingId == listingId);

    public IQueryable<OffExchangeVolume> GetByStock(CommonStock stock, DateOnly date) =>
        GetHistoryByStock(stock).Where(row => row.WeekStartDate == date);

    public IQueryable<OffExchangeVolume> GetByListing(
        CommonStock stock,
        string listedTicker,
        DateOnly date
    ) => GetHistoryByListing(stock, listedTicker).Where(row => row.WeekStartDate == date);

    public IQueryable<OffExchangeVolume> GetHistoryByStock(CommonStock stock) =>
        GetAll()
            .Where(row =>
                row.Listing.Security.EquityIssuerId == stock.Id
                && row.EquityListingId == row.Listing.Security.Issuer.Presentation.EquityListingId
            );

    public virtual IQueryable<OffExchangeVolume> GetHistoryByListing(
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

    public IQueryable<DateOnly> GetLatestWeek()
    {
        return GetAll().LatestValue(d => d.WeekStartDate, distinct: true);
    }

    public IQueryable<DateOnly> GetEarliestWeek()
    {
        return GetAll().Select(d => d.WeekStartDate).Distinct().OrderBy(d => d).Take(1);
    }

    public IQueryable<OffExchangeVolume> GetByWeek(DateOnly weekStartDate)
    {
        return GetAll().Where(d => d.WeekStartDate == weekStartDate);
    }
}
