using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Repositories;

public class FinancialFactsSyncStatusRepository : BaseRepository<FinancialFactsSyncStatus>
{
    public FinancialFactsSyncStatusRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FinancialFactsSyncStatus> GetByIssuerId(Guid issuerId)
    {
        return GetAll().Where(s => s.EquityIssuerId == issuerId);
    }
}
