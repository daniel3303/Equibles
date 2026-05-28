using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Fred.Data.Models;

namespace Equibles.Fred.Repositories;

public class FredObservationRepository : BaseRepository<FredObservation>
{
    public FredObservationRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FredObservation> GetBySeries(FredSeries series)
    {
        return GetAll().Where(o => o.FredSeriesId == series.Id);
    }

    public IQueryable<FredObservation> GetBySeries(
        FredSeries series,
        DateOnly startDate,
        DateOnly endDate
    )
    {
        return GetAll()
            .Where(o => o.FredSeriesId == series.Id && o.Date >= startDate && o.Date <= endDate);
    }

    public IQueryable<DateOnly> GetLatestDate(FredSeries series)
    {
        return GetAll().Where(o => o.FredSeriesId == series.Id).LatestValue(o => o.Date);
    }

    public IQueryable<FredObservation> GetLatestPerSeries()
    {
        return GetAll()
            .Where(o => o.Value != null)
            .GroupBy(o => o.FredSeriesId)
            .Select(g => g.OrderByDescending(o => o.Date).First());
    }
}
