using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Repositories;

public class FinancialFactDimensionRepository : BaseRepository<FinancialFactDimension>
{
    public FinancialFactDimensionRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }
}
