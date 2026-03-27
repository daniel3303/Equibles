using Equibles.Data;
using Equibles.Fred.Data.Models;

namespace Equibles.Fred.Repositories;

public class FredSeriesRepository : BaseRepository<FredSeries> {
    public FredSeriesRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public IQueryable<FredSeries> GetBySeriesId(string seriesId) {
        return GetAll().Where(s => s.SeriesId == seriesId);
    }

    public IQueryable<FredSeries> GetByCategory(FredSeriesCategory category) {
        return GetAll().Where(s => s.Category == category);
    }

    public IQueryable<FredSeries> Search(string query) {
        var lower = query.ToLower();
        return GetAll().Where(s =>
            s.SeriesId.ToLower().Contains(lower) ||
            s.Title.ToLower().Contains(lower));
    }
}
