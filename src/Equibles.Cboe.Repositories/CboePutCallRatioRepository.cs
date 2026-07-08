using Equibles.Cboe.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;

namespace Equibles.Cboe.Repositories;

public class CboePutCallRatioRepository : BaseRepository<CboePutCallRatio>
{
    public CboePutCallRatioRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<CboePutCallRatio> GetByType(CboePutCallRatioType type)
    {
        return GetAll().Where(r => r.RatioType == type);
    }

    public IQueryable<CboePutCallRatio> GetByType(
        CboePutCallRatioType type,
        DateOnly startDate,
        DateOnly endDate
    )
    {
        return GetAll().Where(r => r.RatioType == type && r.Date >= startDate && r.Date <= endDate);
    }

    public IQueryable<DateOnly> GetLatestDate(CboePutCallRatioType type)
    {
        return GetAll().Where(r => r.RatioType == type).LatestValue(r => r.Date);
    }

    public IQueryable<CboePutCallRatio> GetLatestPerType()
    {
        // The latest row for each ratio type. Expressed as a correlated max-date filter rather than
        // GroupBy(...).Select(g => g.OrderByDescending(...).First()), which EF Core cannot translate — it
        // throws KeyNotFoundException "EmptyProjectionMember" at runtime. (RatioType, Date) is unique, so
        // matching the max date per type yields exactly one row per type.
        return GetAll()
            .Where(r => r.Date == GetAll().Where(x => x.RatioType == r.RatioType).Max(x => x.Date));
    }
}
