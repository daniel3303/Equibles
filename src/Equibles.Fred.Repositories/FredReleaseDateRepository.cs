using Equibles.Data;
using Equibles.Fred.Data.Models;

namespace Equibles.Fred.Repositories;

public class FredReleaseDateRepository : BaseRepository<FredReleaseDate>
{
    public FredReleaseDateRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FredReleaseDate> GetByRelease(FredRelease release)
    {
        return GetAll().Where(d => d.FredReleaseId == release.Id);
    }

    public IQueryable<FredReleaseDate> GetInRange(DateOnly startDate, DateOnly endDate)
    {
        return GetAll().Where(d => d.Date >= startDate && d.Date <= endDate);
    }
}
