using System.Linq.Expressions;
using Equibles.Cboe.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;

namespace Equibles.Cboe.Repositories;

public class CboeVixDailyRepository : BaseRepository<CboeVixDaily>
{
    private static readonly Expression<Func<CboeVixDaily, bool>> ValidOhlc = v =>
        v.Open > 0
        && v.High > 0
        && v.Low > 0
        && v.Close > 0
        && v.High >= v.Open
        && v.High >= v.Close
        && v.Low <= v.Open
        && v.Low <= v.Close
        && v.High >= v.Low;

    private static readonly Expression<Func<CboeVixDaily, bool>> InvalidOhlc = v =>
        v.Open <= 0
        || v.High <= 0
        || v.Low <= 0
        || v.Close <= 0
        || v.High < v.Open
        || v.High < v.Close
        || v.Low > v.Open
        || v.Low > v.Close
        || v.High < v.Low;

    public CboeVixDailyRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<CboeVixDaily> GetByDateRange(DateOnly startDate, DateOnly endDate)
    {
        return GetAll().Where(ValidOhlc).Where(v => v.Date >= startDate && v.Date <= endDate);
    }

    public IQueryable<DateOnly> GetLatestDate()
    {
        return GetAll().Where(ValidOhlc).LatestValue(v => v.Date);
    }

    public IQueryable<CboeVixDaily> GetInvalidOhlc() => GetAll().Where(InvalidOhlc);
}
