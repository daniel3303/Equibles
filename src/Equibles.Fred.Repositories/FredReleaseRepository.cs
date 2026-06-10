using Equibles.Data;
using Equibles.Fred.Data.Models;

namespace Equibles.Fred.Repositories;

public class FredReleaseRepository : BaseRepository<FredRelease>
{
    public FredReleaseRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FredRelease> GetByReleaseId(int releaseId)
    {
        return GetAll().Where(r => r.ReleaseId == releaseId);
    }
}
