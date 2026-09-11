using Equibles.CommonStocks.Data.Models;
using Equibles.Data;

namespace Equibles.CommonStocks.Repositories;

public class EquityListingRepository : BaseRepository<EquityListing>
{
    public EquityListingRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }
}
