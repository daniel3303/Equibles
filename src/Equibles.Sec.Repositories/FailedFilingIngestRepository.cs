using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class FailedFilingIngestRepository : BaseRepository<FailedFilingIngest>
{
    public FailedFilingIngestRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FailedFilingIngest> GetByAccessionNumber(string accessionNumber) =>
        GetAll().Where(f => f.AccessionNumber == accessionNumber);

    public IQueryable<FailedFilingIngest> GetByAccessionNumbers(
        IEnumerable<string> accessionNumbers
    ) => GetAll().Where(f => accessionNumbers.Contains(f.AccessionNumber));
}
