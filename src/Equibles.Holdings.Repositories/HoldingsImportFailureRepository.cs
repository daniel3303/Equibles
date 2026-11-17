using Equibles.Data;
using Equibles.Holdings.Data.Extensions;
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

    // Reasons that close a row without a holdings write; any other reason on a resolved row means
    // the filing's holdings were written.
    private static readonly int[] ClosedWithoutWrite =
    [
        (int)HoldingsImportFailureReason.SourceWithdrawn,
        (int)HoldingsImportFailureReason.NoTrackedPositions,
        (int)HoldingsImportFailureReason.ConfidentialTreatment,
        (int)HoldingsImportFailureReason.OutsideHistoryWindow,
    ];

    /// <summary>
    /// The unresolved filings whose rows are not stored yet. A stored filing awaiting its ordered
    /// replay is left out: it delays nothing a reader can see.
    /// </summary>
    /// <summary>
    /// The filings closed as withholding their positions that have no confidential-treatment
    /// report recorded.
    /// </summary>
    public IQueryable<HoldingsImportFailure> GetConfidentialClosuresWithoutReport() =>
        GetAll()
            .Where(row =>
                row.Reason == HoldingsImportFailureReason.ConfidentialTreatment
                && !DbContext
                    .Set<Form13FConfidentialFiling>()
                    .Any(report => report.AccessionNumber == row.AccessionNumber)
            );

    /// <summary>
    /// Restates as <see cref="HoldingsImportFailureReason.StoredReplay"/> every pending recovery
    /// whose filing is stored. A release that did not tell the two apart queued them alike, and
    /// such a backlog would keep deferring the sweep and counting against coverage.
    /// </summary>
    public Task<int> RestateStoredReplays(CancellationToken cancellationToken) =>
        GetAll()
            .Where(row =>
                row.ResolvedAt == null
                && row.Reason == HoldingsImportFailureReason.PendingRecovery
                && (
                    DbContext
                        .Set<InstitutionalHolding>()
                        .Any(holding =>
                            holding.AccessionNumber == row.AccessionNumber
                            && holding.FilingType == FilingType.Form13F
                        )
                    || DbContext
                        .Set<InstitutionalFiling>()
                        .Zero13FRestatements()
                        .Any(filing => filing.AccessionNumber == row.AccessionNumber)
                )
            )
            .ExecuteUpdateAsync(
                update =>
                    update.SetProperty(row => row.Reason, HoldingsImportFailureReason.StoredReplay),
                cancellationToken
            );

    public IQueryable<HoldingsImportFailure> GetWithheld() =>
        GetAll()
            .Where(row =>
                row.ResolvedAt == null && row.Reason != HoldingsImportFailureReason.StoredReplay
            );

    // Discovery is not an import attempt and must not postpone an existing retry. A reopened row
    // drops the reason it closed under, which a later write would otherwise contradict.
    public Task EnqueueRecovery(
        string accession,
        string cik,
        DateOnly filed,
        CancellationToken cancellationToken,
        bool supersedesActiveAttempt = false,
        HoldingsImportFailureReason reason = HoldingsImportFailureReason.PendingRecovery
    ) =>
        DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "HoldingsImportFailure"
              ("AccessionNumber", "Cik", "FilingDate", "Reason", "Attempts",
               "FirstFailedAt", "LastAttemptAt", "NextAttemptAt", "ResolvedAt")
            VALUES ({accession}, {cik}, {filed}, {(int)reason}, 0,
                    clock_timestamp(), clock_timestamp(), clock_timestamp(), NULL)
            ON CONFLICT ("AccessionNumber") DO UPDATE SET
              "ResolvedAt" = NULL, "LastAttemptAt" = clock_timestamp(),
              "NextAttemptAt" = clock_timestamp(),
              "Reason" = CASE WHEN "HoldingsImportFailure"."ResolvedAt" IS NOT NULL
                THEN {(int)reason}
                ELSE "HoldingsImportFailure"."Reason" END
            WHERE "HoldingsImportFailure"."ResolvedAt" IS NOT NULL OR {supersedesActiveAttempt}
            """,
            cancellationToken
        );

    // A reason restates why the row closed without an import (a withdrawn filing); null keeps it.
    public Task Resolve(
        string accession,
        CancellationToken cancellationToken,
        DateTime? attemptedBefore = null,
        HoldingsImportFailureReason? reason = null
    ) =>
        GetAll()
            .Where(row =>
                row.AccessionNumber == accession
                && row.ResolvedAt == null
                && (attemptedBefore == null || row.LastAttemptAt <= attemptedBefore)
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(row => row.ResolvedAt, DateTime.UtcNow)
                        .SetProperty(row => row.Reason, row => reason ?? row.Reason),
                cancellationToken
            );

    /// <summary>
    /// Restates a resolved row after another import of its filing (a realtime replay or a
    /// recovery tail): under the source proof when that import stored nothing, otherwise no longer
    /// under a reason that says nothing was stored.
    /// </summary>
    public Task RestateResolved(
        string accession,
        HoldingsImportFailureReason? closedWithoutRows,
        CancellationToken cancellationToken
    ) =>
        DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "HoldingsImportFailure"
            SET "Reason" = {(int)(closedWithoutRows ?? HoldingsImportFailureReason.PendingRecovery)}
            WHERE "AccessionNumber" = {accession} AND "ResolvedAt" IS NOT NULL
              AND ({closedWithoutRows.HasValue} OR "Reason" = ANY({ClosedWithoutWrite}))
            """,
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
