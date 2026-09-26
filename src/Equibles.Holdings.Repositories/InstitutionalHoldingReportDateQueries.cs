using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

public static class InstitutionalHoldingReportDateQueries
{
    public static IQueryable<DateOnly> Get13FReportDates(EquiblesFinancialDbContext dbContext)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return dbContext
                .Set<InstitutionalHolding>()
                .Where(h => h.FilingType == FilingType.Form13F)
                .Select(h => h.ReportDate)
                .Distinct();
        }

        // DISTINCT reads every position even when the caller only counts quarters. Seek past
        // each date instead; snapshots and filing rollups can lag holdings during an import.
        return dbContext.Database.SqlQuery<DateOnly>(
            $"""
            WITH RECURSIVE report_dates AS (
                (SELECT "ReportDate" AS "Value"
                 FROM "InstitutionalHolding"
                 WHERE "FilingType" = {(int)FilingType.Form13F}
                 ORDER BY "ReportDate" DESC LIMIT 1)
                UNION ALL
                SELECT next_date."Value"
                FROM report_dates previous_date
                CROSS JOIN LATERAL (
                    SELECT "ReportDate" AS "Value"
                    FROM "InstitutionalHolding"
                    WHERE "FilingType" = {(int)FilingType.Form13F}
                      AND "ReportDate" < previous_date."Value"
                    ORDER BY "ReportDate" DESC LIMIT 1
                ) next_date
            )
            SELECT "Value" FROM report_dates
            """
        );
    }
}
