using Equibles.Cboe.Data.Models;
using Equibles.Data;

namespace Equibles.Cboe.Repositories;

public class CboePutCallRatioRepository : BaseRepository<CboePutCallRatio> {
    public CboePutCallRatioRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public IQueryable<CboePutCallRatio> GetByType(CboePutCallRatioType type) {
        return GetAll().Where(r => r.RatioType == type);
    }

    public IQueryable<CboePutCallRatio> GetByType(CboePutCallRatioType type, DateOnly startDate, DateOnly endDate) {
        return GetAll().Where(r =>
            r.RatioType == type &&
            r.Date >= startDate &&
            r.Date <= endDate);
    }

    public IQueryable<DateOnly> GetLatestDate(CboePutCallRatioType type) {
        return GetAll()
            .Where(r => r.RatioType == type)
            .Select(r => r.Date)
            .OrderByDescending(d => d)
            .Take(1);
    }

    public IQueryable<CboePutCallRatio> GetLatestPerType() {
        return GetAll()
            .GroupBy(r => r.RatioType)
            .Select(g => g.OrderByDescending(r => r.Date).First());
    }
}
