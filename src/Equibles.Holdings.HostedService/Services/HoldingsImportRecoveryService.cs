using Equibles.Core.AutoWiring;
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
    ISecEdgarClient edgar,
    Realtime13FIngestionService ingestion,
    ILogger<HoldingsImportRecoveryService> logger
)
{
    private const int FilersPerCycle = 10;

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
                NextAttemptAt = group.Min(row => row.NextAttemptAt),
            })
            .OrderBy(row => row.NextAttemptAt)
            .ThenBy(row => row.Cik)
            .Select(row => row.Cik)
            .Take(FilersPerCycle)
            .ToListAsync(cancellationToken);
        foreach (var cik in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Persist the cooldown first so a crash or unreadable submissions response cannot
            // monopolize every cycle. Failed accessions remain unresolved throughout the batch.
            await failures.Defer(cik, now, cancellationToken);
            try
            {
                await RecoverFiler(cik, minReportDate, cancellationToken);
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
        var filings = await edgar.GetCompanyFilings(
            cik,
            documentType: null,
            fromDate: from,
            toDate: DateOnly.FromDateTime(DateTime.UtcNow)
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
            .GetAll()
            .Where(row =>
                row.InstitutionalHolder.Cik == cik
                && row.FilingType == Equibles.Holdings.Data.Models.FilingType.Form13F
                && row.FilingDate >= from
            )
            .Select(row => row.AccessionNumber)
            .Distinct()
            .ToListAsync(cancellationToken);
        var offeredAccessions = entries
            .Select(entry => entry.AccessionNumber)
            .Concat(outOfScope)
            .ToHashSet();
        if (retainedAccessions.Any(accession => !offeredAccessions.Contains(accession)))
            throw new InvalidDataException(
                "The SEC submissions response omitted a retained later filing; recovery was not attempted."
            );
        // Reapply the complete later tail, even if its markers already exist. Otherwise an
        // old recovered original overwrites amendments imported while that original failed.
        if (
            pending.Any(row =>
                !outOfScope.Contains(row.AccessionNumber)
                && !entries.Any(entry => entry.AccessionNumber == row.AccessionNumber)
            )
        )
            throw new InvalidDataException(
                "The SEC submissions response omitted a pending filing; recovery was not attempted."
            );
        var imported = await ingestion.IngestSpecificFilings(
            entries,
            minReportDate,
            cancellationToken
        );
        if (imported != entries.Count)
            return;
        foreach (var row in pending)
            await failures.Resolve(row.AccessionNumber, cancellationToken, startedAt);
        logger.LogInformation(
            "Holdings recovery resolved {Failures} failed filings for CIK {Cik}; replayed {Filings} filings",
            pending.Count,
            cik,
            imported
        );
    }
}
