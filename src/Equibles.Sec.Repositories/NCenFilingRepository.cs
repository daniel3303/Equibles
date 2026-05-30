using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class NCenFilingRepository : SecFilingRepositoryBase<NCenFiling>
{
    public NCenFilingRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }
}
