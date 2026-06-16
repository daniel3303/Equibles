using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class ProcessedNportFilingRepository : BaseRepository<ProcessedNportFiling>
{
    public ProcessedNportFilingRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<ProcessedNportFiling> GetByAccessionNumbers(
        IReadOnlyCollection<string> accessionNumbers
    )
    {
        return GetAll().Where(p => accessionNumbers.Contains(p.AccessionNumber));
    }
}
