using Equibles.Data;
using Equibles.InsiderTrading.Data.Models;

namespace Equibles.InsiderTrading.Repositories;

public class InsiderFilingRepository : BaseRepository<InsiderFiling>
{
    public InsiderFilingRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<InsiderFiling> GetByAccessionNumber(string accessionNumber)
    {
        return GetAll().Where(f => f.AccessionNumber == accessionNumber);
    }
}
