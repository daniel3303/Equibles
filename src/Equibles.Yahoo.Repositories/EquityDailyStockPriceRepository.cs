using Equibles.Data;
using Equibles.Yahoo.Data.Models;

namespace Equibles.Yahoo.Repositories;

public class EquityDailyStockPriceRepository : BaseRepository<EquityDailyStockPrice>
{
    public EquityDailyStockPriceRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<EquityDailyStockPrice> GetByListing(Guid listingId) =>
        GetAll().Where(price => price.EquityListingId == listingId);
}
