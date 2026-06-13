using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.FdaCatalysts.Data.Models;

namespace Equibles.FdaCatalysts.Repositories;

public class FdaCatalystRepository : BaseRepository<FdaCatalyst>
{
    public FdaCatalystRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FdaCatalyst> GetUpcoming(DateOnly from)
    {
        return GetAll().Where(c => c.MeetingDate >= from);
    }

    public IQueryable<FdaCatalyst> GetByDateRange(DateOnly startDate, DateOnly endDate)
    {
        return GetAll().Where(c => c.MeetingDate >= startDate && c.MeetingDate <= endDate);
    }

    public IQueryable<FdaCatalyst> GetByStock(Guid commonStockId)
    {
        return GetAll().Where(c => c.CommonStockId == commonStockId);
    }

    public IQueryable<DateOnly> GetLatestMeetingDate()
    {
        return GetAll().LatestValue(c => c.MeetingDate);
    }
}
