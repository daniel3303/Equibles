using Equibles.Core.AutoWiring;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>Retries a bounded set of failed filers independently of the moving daily-index window.</summary>
[Service]
public class HoldingsImportRecoveryService(
    HoldingsImportFailureRepository failures,
    InstitutionalHoldingRepository holdings,
    RealtimeSweepStateRepository sweepStates,
    ISecEdgarClient edgar,
    Realtime13FIngestionService ingestion,
    ILogger<HoldingsImportRecoveryService> logger
)
{
    private const int FilersPerCycle = 10;

    // SEC's per-filer filing list lags acceptance by minutes. A filing discovered within the
    // window retries on the short interval; an older one keeps the filer's ordinary cooldown.
    internal static readonly TimeSpan ListingLagRetry = TimeSpan.FromMinutes(3);
    internal static readonly TimeSpan ListingLagWindow = TimeSpan.FromHours(1);

    public async Task Recover(DateOnly minReportDate, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var due = await failures
            .GetAll()
            .Where(row => row.ResolvedAt == null && row.NextAttemptAt <= now)
            .GroupBy(row => row.Cik)
            .Select(group => new
            {
                Cik = group.Key,
                // 0 while a due filing is not stored yet, so its manager goes before the managers
                // that only owe a replay of stored filings.
                ReplayOnly = group.Min(row =>
                    row.Reason == HoldingsImportFailureReason.StoredReplay ? 1 : 0
                ),
                NextAttemptAt = group.Min(row => row.NextAttemptAt),
            })
            .OrderBy(row => row.ReplayOnly)
            .ThenBy(row => row.NextAttemptAt)
            .ThenBy(row => row.Cik)
            .Select(row => row.Cik)
            .Take(FilersPerCycle)
            .ToListAsync(cancellationToken);
        var sweptThrough = await sweepStates
            .GetByWorker(Holdings13FRealtimeWorker.WorkerStateName)
            .Select(state => (DateOnly?)state.SweptThrough)
            .SingleOrDefaultAsync(cancellationToken);
        foreach (var cik in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Persist the cooldown first so a crash or unreadable submissions response cannot
            // monopolize every cycle. Failed accessions remain unresolved throughout the batch.
            await failures.Defer(cik, now, cancellationToken);
            try
            {
                await RecoverFiler(cik, minReportDate, sweptThrough, cancellationToken);
            }
            catch (PendingFilingNotListedException)
            {
                // Expected minutes after acceptance; only a filing discovered recently retries soon.
                var retried = await failures.RetrySoon(
                    cik,
                    now - ListingLagWindow,
                    now + ListingLagRetry,
                    cancellationToken
                );
                logger.LogInformation(
                    "Holdings recovery for CIK {Cik} waits for SEC's filing list; {Retried} filings retry in {Minutes} minutes",
                    cik,
                    retried,
                    ListingLagRetry.TotalMinutes
                );
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "Holdings recovery for CIK {Cik} remains pending",
                    cik
                );
            }
        }
    }

    private async Task RecoverFiler(
        string cik,
        DateOnly minReportDate,
        DateOnly? sweptThrough,
        CancellationToken cancellationToken
    )
    {
        var pending = await failures
            .GetAll()
            .Where(row => row.Cik == cik && row.ResolvedAt == null)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
            return;
        var startedAt = DateTime.UtcNow;
        var from = pending.Min(row => row.FilingDate);
        // The sweep has yet to re-apply this manager's filings from where it resumes: its cursor,
        // or its trailing days when the cursor is newer. A tail that starts later marks the
        // pending filing processed, and the sweep then re-applies an older filing over it.
        if (sweptThrough != null)
        {
            var sweepResumes = Holdings13FRealtimeWorker.ComputeWindowStart(
                DateOnly.FromDateTime(startedAt),
                sweptThrough,
                Holdings13FRealtimeWorker.TrailingReSweepDays,
                0
            );
            if (sweepResumes < from)
                from = sweepResumes;
        }
        var filings = await edgar.GetCompanyFilings(
            cik,
            edgarForm: null,
            // No upper bound: a filing accepted after 17:30 ET is dated the next business day.
            fromDate: from,
            toDate: null
        );
        var outOfScope = filings
            .Where(f => f.ReportDate != default && f.ReportDate < minReportDate)
            .Select(f => f.AccessionNumber)
            .ToHashSet();
        var entries = filings
            .Where(f => f.Form is "13F-HR" or "13F-HR/A")
            .Where(f => !outOfScope.Contains(f.AccessionNumber))
            .Select(f => new EdgarDailyIndexEntry
            {
                Cik = cik,
                AccessionNumber = f.AccessionNumber,
                DateFiled = f.FilingDate,
                FormType = f.Form,
            })
            .DistinctBy(entry => entry.AccessionNumber)
            .ToList();
        var retainedAccessions = await holdings
            .GetRetained13FAccessions(cik, from)
            .ToListAsync(cancellationToken);
        var offeredAccessions = entries
            .Select(entry => entry.AccessionNumber)
            .Concat(outOfScope)
            .ToHashSet();
        var omitted = pending
            .Where(row =>
                !outOfScope.Contains(row.AccessionNumber)
                && !entries.Any(entry => entry.AccessionNumber == row.AccessionNumber)
            )
            .ToList();
        // A withdrawn filing whose rows are still published is refused too: removing them is
        // an operator decision, so it keeps its own message.
        if (retainedAccessions.Any(accession => !offeredAccessions.Contains(accession)))
            throw new InvalidDataException(
                omitted.Any(row => retainedAccessions.Contains(row.AccessionNumber))
                    ? "The SEC submissions response omitted a pending filing whose rows are retained; recovery was not attempted."
                    : "The SEC submissions response omitted a retained later filing; recovery was not attempted."
            );
        foreach (var row in omitted)
        {
            // EDGAR removes a filing the filer withdraws or SEC rejects after acceptance, and its
            // directory goes with it; a directory still published means the index only lags.
            if (await edgar.FilingExists(cik, row.AccessionNumber, cancellationToken))
                throw new PendingFilingNotListedException(
                    "The SEC submissions response omitted a pending filing EDGAR still publishes; recovery was not attempted."
                );
            await failures.Resolve(
                row.AccessionNumber,
                cancellationToken,
                startedAt,
                HoldingsImportFailureReason.SourceWithdrawn
            );
            logger.LogInformation(
                "Resolved 13F {Accession} (CIK {Cik}) as withdrawn: EDGAR no longer publishes it",
                row.AccessionNumber,
                cik
            );
        }
        pending.RemoveAll(omitted.Contains);
        if (pending.Count == 0)
            return;
        // Reapply the later tail after any filing that changes holdings, even if its markers
        // already exist. Otherwise an old recovered original overwrites amendments imported while
        // that original failed.
        var replay = await ingestion.ReplayRecoveryTail(
            entries,
            pending.Select(row => row.AccessionNumber).ToHashSet(StringComparer.OrdinalIgnoreCase),
            minReportDate,
            cancellationToken
        );
        var resolved = replay.Complete
            ? pending.Select(row => row.AccessionNumber).ToList()
            : replay.Resolvable;
        // A row closed without storing anything names the source's proof, so a resolved row that
        // kept its failure reason always means the filing's holdings were written.
        foreach (var accession in resolved)
            await failures.Resolve(
                accession,
                cancellationToken,
                startedAt,
                outOfScope.Contains(accession) ? HoldingsImportFailureReason.OutsideHistoryWindow
                    : replay.ClosedWithoutRows.TryGetValue(accession, out var closure) ? closure
                    : null
            );
        if (resolved.Count == 0)
        {
            logger.LogDebug(
                "Holdings recovery for CIK {Cik} remains pending; attempted {Attempted} filings, skipped {Skipped} unchanged",
                cik,
                replay.Attempted,
                replay.Skipped
            );
            return;
        }
        logger.LogInformation(
            "Holdings recovery resolved {Resolved} of {Failures} failed filings for CIK {Cik}; replayed {Filings} filings, skipped {Skipped} unchanged",
            resolved.Count,
            pending.Count,
            cik,
            replay.Imported,
            replay.Skipped
        );
    }
}
