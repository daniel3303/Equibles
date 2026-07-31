using Equibles.Data;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Repositories;

public class FilingOtherManagerRepository : BaseRepository<FilingOtherManager>
{
    public FilingOtherManagerRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FilingOtherManager> GetByAccessionNumbers(
        IEnumerable<string> accessionNumbers
    )
    {
        return GetAll().Where(m => accessionNumbers.Contains(m.AccessionNumber));
    }
}
