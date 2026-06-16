using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

/// <summary>
/// Reads the materialised fund directory (<see cref="FundSeries"/>). One row per registered-fund
/// series, rebuilt by the refresh worker — these queries are plain indexed lookups, never the live
/// "latest report per series" scan over <see cref="NportFiling"/>.
/// </summary>
public class FundSeriesRepository : BaseRepository<FundSeries>
{
    public FundSeriesRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>The series with the given route slug (empty result when unknown).</summary>
    public IQueryable<FundSeries> GetBySlug(string slug)
    {
        return GetAll().Where(s => s.Slug == slug);
    }
}
