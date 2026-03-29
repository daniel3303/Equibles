using Equibles.Cboe.Data.Models;
using Equibles.Data;

namespace Equibles.Cboe.Repositories;

public class CboeVixDailyRepository : BaseRepository<CboeVixDaily> {
    public CboeVixDailyRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public IQueryable<CboeVixDaily> GetByDateRange(DateOnly startDate, DateOnly endDate) {
        return GetAll().Where(v => v.Date >= startDate && v.Date <= endDate);
    }

    public IQueryable<DateOnly> GetLatestDate() {
        return GetAll()
            .Select(v => v.Date)
            .OrderByDescending(d => d)
            .Take(1);
    }
}
