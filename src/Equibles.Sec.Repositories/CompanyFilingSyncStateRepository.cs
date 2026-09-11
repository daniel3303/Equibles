using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class CompanyFilingSyncStateRepository : BaseRepository<CompanyFilingSyncState>
{
    public CompanyFilingSyncStateRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<CompanyFilingSyncState> GetByIssuerId(Guid issuerId) =>
        GetAll().Where(s => s.EquityIssuerId == issuerId);
}
