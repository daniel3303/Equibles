using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Finra.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Finra.Repositories;

public class ShortInterestRepository : BaseRepository<ShortInterest>
{
    public ShortInterestRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<ShortInterest> GetByStock(CommonStock stock, DateOnly settlementDate)
    {
        return GetAll()
            .Where(s => s.CommonStockId == stock.Id && s.SettlementDate == settlementDate);
    }

    public IQueryable<ShortInterest> GetHistoryByStock(CommonStock stock)
    {
        return GetAll().Where(s => s.CommonStockId == stock.Id);
    }

    public IQueryable<DateOnly> GetLatestSettlementDate()
    {
        return GetAll().LatestValue(s => s.SettlementDate, distinct: true);
    }

    public IQueryable<ShortInterest> GetBySettlementDate(DateOnly settlementDate)
    {
        return GetAll().Where(s => s.SettlementDate == settlementDate);
    }

    public IQueryable<DateOnly> GetAllSettlementDates()
    {
        // A plain SELECT DISTINCT "SettlementDate" scans the entire SettlementDate
        // index (one entry per row, ~hundreds of thousands), so it is slow and spikes
        // when the buffer cache is cold. The settlement dates are a tiny set (a few
        // hundred), so we walk them with a loose index ("skip") scan instead: jump to
        // the newest date, then repeatedly fetch the next-lower one via the same
        // IX_ShortInterest_SettlementDate index. That touches only one index entry per
        // distinct date. Returned newest-first; callers need not re-sort.
        if (!DbContext.Database.IsRelational())
        {
            // Non-relational providers (e.g. the in-memory provider used in tests)
            // cannot run raw SQL — fall back to the equivalent DISTINCT query.
            return GetAll().Select(s => s.SettlementDate).Distinct().OrderByDescending(d => d);
        }

        return DbContext.Database.SqlQueryRaw<DateOnly>(
            """
            WITH RECURSIVE "dates" AS (
                SELECT (
                    SELECT "SettlementDate"
                    FROM "ShortInterest"
                    ORDER BY "SettlementDate" DESC
                    LIMIT 1
                ) AS "Value"
                UNION ALL
                SELECT (
                    SELECT "SettlementDate"
                    FROM "ShortInterest"
                    WHERE "SettlementDate" < "dates"."Value"
                    ORDER BY "SettlementDate" DESC
                    LIMIT 1
                ) AS "Value"
                FROM "dates"
                WHERE "dates"."Value" IS NOT NULL
            )
            SELECT "Value" FROM "dates" WHERE "Value" IS NOT NULL
            """
        );
    }

    public IQueryable<Guid> GetStockIdsBySettlementDate(DateOnly settlementDate)
    {
        return GetAll().Where(s => s.SettlementDate == settlementDate).Select(s => s.CommonStockId);
    }
}
