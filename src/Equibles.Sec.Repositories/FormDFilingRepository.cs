using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class FormDFilingRepository : SecFilingRepositoryBase<FormDFiling>
{
    public FormDFilingRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }
}
