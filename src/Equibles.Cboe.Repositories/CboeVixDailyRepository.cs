using Equibles.Cboe.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;

namespace Equibles.Cboe.Repositories;

public class CboeVixDailyRepository : BaseRepository<CboeVixDaily>
{
    public CboeVixDailyRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<CboeVixDaily> GetByDateRange(DateOnly startDate, DateOnly endDate)
    {
        return GetAll().Where(v => v.Date >= startDate && v.Date <= endDate);
    }

    public IQueryable<DateOnly> GetLatestDate()
    {
        return GetAll().LatestValue(v => v.Date);
    }
}
