using Equibles.CommonStocks.Data.Models;
using Equibles.Data;

namespace Equibles.CommonStocks.Repositories;

public class EquityListingSymbolStartRepository : BaseRepository<EquityListingSymbolStart>
{
    public EquityListingSymbolStartRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<EquityListingSymbolStart> GetBySource(string evidenceSource) =>
        GetAll().Where(start => start.EvidenceSource == evidenceSource);
}
