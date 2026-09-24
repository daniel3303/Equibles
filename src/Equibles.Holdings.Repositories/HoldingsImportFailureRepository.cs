using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

public class HoldingsImportFailureRepository(EquiblesFinancialDbContext dbContext)
    : BaseRepository<HoldingsImportFailure>(dbContext)
{
    public Task Record(
        string accession,
        string cik,
        DateOnly filed,
        DateOnly? reportDate,
        HoldingsImportFailureReason reason,
        CancellationToken cancellationToken
    ) =>
        DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "HoldingsImportFailure"
              ("AccessionNumber", "Cik", "FilingDate", "ReportDate", "Reason", "Attempts",
               "FirstFailedAt", "LastAttemptAt", "NextAttemptAt", "ResolvedAt")
            VALUES ({accession}, {cik}, {filed}, {reportDate}, {(int)reason}, 1,
                    clock_timestamp(), clock_timestamp(), clock_timestamp(), NULL)
            ON CONFLICT ("AccessionNumber") DO UPDATE SET
              "Reason" = EXCLUDED."Reason", "ReportDate" = COALESCE(EXCLUDED."ReportDate", "HoldingsImportFailure"."ReportDate"),
              "Attempts" = "HoldingsImportFailure"."Attempts" + 1,
              "LastAttemptAt" = clock_timestamp(), "NextAttemptAt" = clock_timestamp() + interval '6 hours',
              "ResolvedAt" = NULL
            """,
            cancellationToken
        );

    public Task Resolve(
        string accession,
        CancellationToken cancellationToken,
        DateTime? attemptedBefore = null
    ) =>
        GetAll()
            .Where(row =>
                row.AccessionNumber == accession
                && row.ResolvedAt == null
                && (attemptedBefore == null || row.LastAttemptAt <= attemptedBefore)
            )
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.ResolvedAt, DateTime.UtcNow),
                cancellationToken
            );

    public Task Defer(string cik, DateTime attemptedAt, CancellationToken cancellationToken) =>
        GetAll()
            .Where(row => row.Cik == cik && row.ResolvedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.NextAttemptAt, attemptedAt.AddHours(6)),
                cancellationToken
            );
}
