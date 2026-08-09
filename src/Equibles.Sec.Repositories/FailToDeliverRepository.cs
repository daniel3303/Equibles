using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class FailToDeliverRepository : BaseRepository<FailToDeliver>
{
    public FailToDeliverRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FailToDeliver> GetByStock(CommonStock stock)
    {
        return GetAll().Where(f => f.CommonStockId == stock.Id);
    }

    public IQueryable<DateOnly> GetLatestDate()
    {
        return GetAll().LatestValue(f => f.SettlementDate, distinct: true);
    }

    /// <summary>
    /// A settlement date only counts as covered when its per-date row count reaches this
    /// threshold. The table holds two eras: a sparse pre-full-universe trickle (a handful
    /// of rows per YEAR, single tickers) and full-universe ingestion (thousands of rows
    /// per DAY) — a raw MIN over both converts years of partial ingestion into an
    /// "absent = no reported fails" claim. The threshold measures OUR ingestion
    /// completeness, not the data itself (it is not a data-classification heuristic), so
    /// the authoritative-data rule does not apply.
    /// </summary>
    public const int MinRowsPerCoveredDate = 50;

    /// <summary>
    /// Cache key + duration for the dense-coverage floor: the query scans and groups the
    /// whole table, so every surface reads it through a per-process memory cache. The
    /// floor only ever moves when ingestion history changes, so a long TTL is safe.
    /// </summary>
    public const string DenseCoverageFloorCacheKey = "FailToDeliver:DenseCoverageFloor";
    public static readonly TimeSpan DenseCoverageFloorCacheDuration = TimeSpan.FromHours(6);

    /// <summary>
    /// The earliest settlement date with full-universe ingestion (per-date row count at
    /// least <see cref="MinRowsPerCoveredDate"/>) — the data's dense-coverage floor.
    /// Absence of a date can only be read as "no reported fails" AT OR AFTER this date;
    /// earlier dates are at best partially covered, and surfaces must scope the absence
    /// claim with this value. Empty result when no date qualifies yet.
    /// </summary>
    public IQueryable<DateOnly> GetDenseCoverageFloor()
    {
        return GetAll()
            .GroupBy(f => f.SettlementDate)
            .Where(g => g.Count() >= MinRowsPerCoveredDate)
            .Select(g => g.Key)
            .OrderBy(d => d)
            .Take(1);
    }
}
