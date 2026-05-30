using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class NportFilingRepository : SecFilingRepositoryBase<NportFiling>
{
    public NportFilingRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<NportHolding> GetHoldings(NportFiling filing)
    {
        return DbContext.Set<NportHolding>().Where(h => h.NportFilingId == filing.Id);
    }
}
