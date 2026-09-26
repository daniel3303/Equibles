using System.Data;
using System.Text;
using System.Xml;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Near-real-time 13F-HR ingestion. Discovers freshly accepted 13F-HR /
/// 13F-HR/A submissions from EDGAR's daily index, parses their raw XML, and
/// feeds them through the existing bulk-dataset import pipeline so they
/// reconcile cleanly when the authoritative quarterly data set lands.
/// </summary>
[Service]
public class Realtime13FIngestionService
{
    private readonly ISecEdgarClient _edgarClient;
    private readonly Filing13FXmlParser _parser;
    private readonly Realtime13FArchiveBuilder _archiveBuilder;
    private readonly HoldingsImportService _importService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Realtime13FIngestionService> _logger;

    public Realtime13FIngestionService(
        ISecEdgarClient edgarClient,
        Filing13FXmlParser parser,
        Realtime13FArchiveBuilder archiveBuilder,
        HoldingsImportService importService,
        IServiceScopeFactory scopeFactory,
        ILogger<Realtime13FIngestionService> logger
    )
    {
        _edgarClient = edgarClient;
        _parser = parser;
        _archiveBuilder = archiveBuilder;
        _importService = importService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Sweeps the last <paramref name="lookbackDays"/> days of EDGAR's daily
    /// index (inclusive of <paramref name="today"/>), ingesting every new
    /// 13F-HR submission whose report period is on/after
    /// <paramref name="minReportDate"/>. Returns the number of filings handed
    /// to the import pipeline.
    /// </summary>
    public async Task<RealtimeIngestionResult> IngestRecentFilings(
        DateOnly today,
        int lookbackDays,
        DateOnly minReportDate,
        CancellationToken cancellationToken,
        RealtimeSweepState sweepState = null,
        int maxFilings = int.MaxValue
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFilings, 1);
        var (entries, earliestFailedDate) = await DiscoverEntries(
            today,
            lookbackDays,
            cancellationToken
        );
        if (entries.Count == 0)
        {
            _logger.LogInformation("No 13F-HR submissions found in daily index window");
            return new RealtimeIngestionResult(0, earliestFailedDate);
        }

        var alreadyProcessed = await LoadProcessedAccessions(
            entries.Select(e => e.AccessionNumber),
            cancellationToken
        );

        await using var failureScope = _scopeFactory.CreateAsyncScope();
        var failures =
            failureScope.ServiceProvider.GetRequiredService<HoldingsImportFailureRepository>();
        var pendingRows = await failures
            .GetAll()
            .Where(row => row.ResolvedAt == null)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var recoveringManagers = pendingRows.Select(row => row.Cik.TrimStart('0')).ToHashSet();
        var retryFrom = new Dictionary<string, DateOnly>();

        // Sort chronologically so originals are always imported before their
        // amendments — HandleAmendments in the import pipeline deletes prior
        // holdings for the same holder+period before inserting the amendment.
        var sorted = entries
            .OrderBy(e => e.DateFiled)
            .ThenBy(e => e.AccessionNumber, StringComparer.Ordinal)
            .ToList();

        // Seeded with the discovery-phase failure; per-filing import failures
        // below pull it further back so the watermark re-covers them too.
        var earliestRetryDate = earliestFailedDate;

        var totalImported = 0;
        var attempted = 0;
        foreach (var entry in sorted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Recovery owns the complete ordered tail for these managers. Persist new
            // discoveries before advancing the sweep, including filings older than its floor.
            if (recoveringManagers.Contains(entry.Cik.TrimStart('0')))
            {
                if (!alreadyProcessed.Contains(entry.AccessionNumber))
                    await failures.EnqueueRecovery(
                        entry.AccessionNumber,
                        entry.Cik.TrimStart('0'),
                        entry.DateFiled,
                        cancellationToken
                    );
                continue;
            }

            if (
                alreadyProcessed.Contains(entry.AccessionNumber)
                && !(
                    retryFrom.TryGetValue(entry.Cik.TrimStart('0'), out var retryDate)
                    && entry.DateFiled >= retryDate
                )
            )
            {
                _logger.LogDebug(
                    "Skipping already-processed filing {Accession}",
                    entry.AccessionNumber
                );
                continue;
            }

            // Persist progress only before the first unattempted date. The accession
            // ledger resumes a partly processed day without losing its amendments.
            if (attempted == maxFilings)
            {
                var resumeFrom =
                    earliestRetryDate.HasValue && earliestRetryDate.Value < entry.DateFiled
                        ? earliestRetryDate.Value
                        : entry.DateFiled;
                _logger.LogInformation(
                    "13F real-time ingestion yielded after {Count} attempts; resuming from {Date:yyyy-MM-dd}",
                    attempted,
                    resumeFrom
                );
                return new RealtimeIngestionResult(totalImported, resumeFrom, true);
            }

            attempted++;
            var outcome = await ImportEntry(entry, minReportDate, cancellationToken);
            if (outcome is EntryImportOutcome.Failed or EntryImportOutcome.Incomplete)
            {
                retryFrom[entry.Cik.TrimStart('0')] = entry.DateFiled;
            }
            if (outcome == EntryImportOutcome.Failed)
            {
                // Hold the watermark back to this day so the filing is re-swept
                // next cycle even after it ages out of the trailing window
                // (EquiblesCommercial#2850).
                if (earliestRetryDate == null || entry.DateFiled < earliestRetryDate)
                    earliestRetryDate = entry.DateFiled;
                continue;
            }
            if (outcome is not (EntryImportOutcome.Imported or EntryImportOutcome.Skipped))
                continue;

            // A source-confirmed out-of-range filing is complete too; otherwise it
            // consumes every bounded pass without ever moving the daily frontier.
            if (!await RecordProcessed([entry.AccessionNumber], cancellationToken, sweepState))
                return new RealtimeIngestionResult(totalImported, entry.DateFiled);
            if (outcome == EntryImportOutcome.Imported)
                totalImported++;
        }

        // Daily indexes can be incomplete even when every discovered filing imports.
        // Only complete company-tail recovery may resolve durable failure records.

        _logger.LogInformation(
            "13F real-time ingestion cycle complete: {Count} filings imported",
            totalImported
        );

        return new RealtimeIngestionResult(totalImported, earliestRetryDate);
    }

    /// <summary>
    /// Force-imports a specific set of filings discovered out of band — the
    /// reconciliation worker re-feeding 13F-HRs that EDGAR lists but our holdings
    /// are missing. Unlike <see cref="IngestRecentFilings"/> this does NOT consult
    /// the processed-accession set: a filing recorded "processed" yet holding no
    /// rows (e.g. one a cross-type amendment wiped) must stay re-importable. The
    /// shared import path is idempotent on its upsert key, so re-importing a
    /// filing whose rows are already present is a no-op. Returns the number of
    /// filings that imported with holdings.
    /// </summary>
    public virtual async Task<int> IngestSpecificFilings(
        IReadOnlyCollection<EdgarDailyIndexEntry> entries,
        DateOnly minReportDate,
        CancellationToken cancellationToken
    )
    {
        // Originals before amendments — same ordering contract as the sweep.
        var sorted = entries
            .OrderBy(e => e.DateFiled)
            .ThenBy(e => e.AccessionNumber, StringComparer.Ordinal)
            .ToList();

        var imported = 0;
        foreach (var entry in sorted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = await ImportEntry(entry, minReportDate, cancellationToken);
            if (outcome != EntryImportOutcome.Imported)
                continue;

            await RecordProcessed([entry.AccessionNumber], cancellationToken);
            imported++;
        }

        // A caller may supply only a subset of the manager's filings. Recovery owns
        // resolution after verifying and importing the complete later filing tail.
        return imported;
    }

    private enum EntryImportOutcome
    {
        // Source confirms the filing is outside the configured history boundary.
        Skipped,

        // Imported but the service flagged it incomplete (retry-later, e.g. CUSIPs
        // not seeded). A durable record keeps it retryable outside the sweep window.
        Incomplete,

        // The import threw, or a non-amendment original imported "complete" yet
        // inserted zero holdings — treat as transient and retry.
        Failed,

        // Imported successfully with holdings (or a holdings-removing amendment).
        Imported,
    }

    /// <summary>
    /// Parses, validates and imports one daily-index entry through the shared
    /// bulk-dataset pipeline, free of the caller's bookkeeping (processed-set,
    /// watermark) so the sweep and the reconciliation re-feed share one path.
    /// </summary>
    private async Task<EntryImportOutcome> ImportEntry(
        EdgarDailyIndexEntry entry,
        DateOnly minReportDate,
        CancellationToken cancellationToken
    )
    {
        var filing = await TryParseAndValidateEntry(entry, minReportDate, cancellationToken);
        if (filing == null)
        {
            await RecordFailure(
                entry,
                null,
                HoldingsImportFailureReason.UnreadableSource,
                cancellationToken
            );
            return EntryImportOutcome.Failed;
        }
        if (filing.PeriodOfReport < minReportDate)
            return EntryImportOutcome.Skipped;

        _logger.LogInformation(
            "Importing 13F-HR {Accession} (CIK {Cik}, period {Period})",
            entry.AccessionNumber,
            entry.Cik,
            filing.PeriodOfReport
        );

        ImportResult importResult;
        try
        {
            using var archive = _archiveBuilder.Build([filing]);
            importResult = Filing13FZeroPositionEvidence.IsRestatement(filing)
                ? await _importService.ImportZeroRestatement(
                    filing,
                    minReportDate,
                    cancellationToken
                )
                : await _importService.ImportDataSet(archive, minReportDate, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A poisoned filing must cost only its own rows, never the sweep
            // (EquiblesCommercial#2510): skip it and leave it unrecorded so a
            // later cycle retries it.
            _logger.LogError(
                ex,
                "Failed to import 13F-HR {Accession} (CIK {Cik}); skipping filing",
                entry.AccessionNumber,
                entry.Cik
            );
            await RecordFailure(
                entry,
                filing.PeriodOfReport,
                HoldingsImportFailureReason.ImportFailed,
                cancellationToken
            );
            return EntryImportOutcome.Failed;
        }

        if (importResult.ConflictedFilings.Count > 0)
        {
            await RecordFailure(
                entry,
                filing.PeriodOfReport,
                HoldingsImportFailureReason.IdentityConflict,
                cancellationToken
            );
            return EntryImportOutcome.Failed;
        }
        if (!importResult.IsComplete)
        {
            if (
                importResult.NoTrackedStocks
                && IsSourceConfirmedZeroOriginal(filing)
                && !await HasRetainedBook(filing, cancellationToken)
            )
            {
                _logger.LogInformation(
                    "Completed source-confirmed zero-position 13F {Accession} (CIK {Cik})",
                    entry.AccessionNumber,
                    entry.Cik
                );
                return EntryImportOutcome.Imported;
            }
            await RecordFailure(
                entry,
                filing.PeriodOfReport,
                HoldingsImportFailureReason.Incomplete,
                cancellationToken
            );
            return EntryImportOutcome.Incomplete;
        }

        // A non-amendment original that imported "complete" yet inserted zero
        // holdings is suspect: recording it would consume the filing forever even
        // though we hold none of its positions (the silent permanent loss behind
        // the missing-BlackRock gap). Treat it as transient so the sweep retries
        // and holds the watermark back. Amendments legitimately remove holdings,
        // so they are exempt.
        if (!filing.IsAmendment && importResult.InsertedHoldings == 0)
        {
            _logger.LogWarning(
                "13F-HR {Accession} (CIK {Cik}) imported as complete but inserted no holdings; not recording so a later cycle retries",
                entry.AccessionNumber,
                entry.Cik
            );
            await RecordFailure(
                entry,
                filing.PeriodOfReport,
                HoldingsImportFailureReason.EmptyImport,
                cancellationToken
            );
            return EntryImportOutcome.Failed;
        }

        return EntryImportOutcome.Imported;
    }

    internal static bool IsSourceConfirmedZeroOriginal(Parsed13FFiling filing) =>
        Filing13FZeroPositionEvidence.IsOriginal(filing);

    private async Task<bool> HasRetainedBook(
        Parsed13FFiling filing,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var holdings = scope.ServiceProvider.GetRequiredService<InstitutionalHoldingRepository>();
        return await holdings
            .GetAll()
            .AnyAsync(
                h =>
                    h.InstitutionalHolder.Cik == filing.Cik
                    && h.ReportDate == filing.PeriodOfReport
                    && h.FilingType == FilingType.Form13F,
                cancellationToken
            );
    }

    private async Task RecordFailure(
        EdgarDailyIndexEntry entry,
        DateOnly? reportDate,
        HoldingsImportFailureReason reason,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<HoldingsImportFailureRepository>()
            .Record(
                entry.AccessionNumber,
                entry.Cik.TrimStart('0'),
                entry.DateFiled,
                reportDate,
                reason,
                cancellationToken
            );
    }

    private async Task<Parsed13FFiling> TryParseAndValidateEntry(
        EdgarDailyIndexEntry entry,
        DateOnly minReportDate,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var filing = await ParseFiling(entry, cancellationToken);
            if (filing == null)
                return null;

            if (filing.PeriodOfReport == DateOnly.MinValue)
            {
                _logger.LogWarning(
                    "Skipping filing {Accession}: unparseable report period",
                    entry.AccessionNumber
                );
                return null;
            }

            return filing;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One malformed filing must not abort the whole sweep — the
            // quarterly bulk import will still reconcile it later.
            _logger.LogError(
                ex,
                "Failed to ingest 13F filing {Accession} (CIK {Cik}), continuing",
                entry.AccessionNumber,
                entry.Cik
            );
            return null;
        }
    }

    private async Task<(
        List<EdgarDailyIndexEntry> Entries,
        DateOnly? EarliestFailedDate
    )> DiscoverEntries(DateOnly today, int lookbackDays, CancellationToken cancellationToken)
    {
        // Deduplicate by accession across the window: an amendment carries a
        // distinct accession number, so this only collapses the same filing
        // re-listed across overlapping sweeps, never an original vs amendment.
        var byAccession = new Dictionary<string, EdgarDailyIndexEntry>(
            StringComparer.OrdinalIgnoreCase
        );

        // The oldest day this cycle failed to fetch. Offsets increase (dates run
        // backwards), so the last failure seen is the earliest date — the worker
        // holds the sweep watermark back to before it so it is re-swept next time.
        DateOnly? earliestFailed = null;

        for (var offset = 0; offset < lookbackDays; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var date = today.AddDays(-offset);

            // One day's fetch failing (a transient error, or SEC throttling that
            // outlasts the client's retries) must not abort the whole sweep and
            // lose every other day. Log it, remember it, and move on.
            List<EdgarDailyIndexEntry> indexEntries;
            try
            {
                indexEntries = await _edgarClient.GetDailyIndex(date, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to fetch the {Date:yyyy-MM-dd} daily index; skipping this day",
                    date
                );
                earliestFailed = date;
                continue;
            }

            // The client already filters to 13F-HR / 13F-HR/A; only guard
            // against a malformed row with no accession number here.
            foreach (var entry in indexEntries)
            {
                if (!string.IsNullOrEmpty(entry.AccessionNumber))
                    byAccession[entry.AccessionNumber] = entry;
            }
        }

        return (byAccession.Values.ToList(), earliestFailed);
    }

    private async Task<HashSet<string>> LoadProcessedAccessions(
        IEnumerable<string> accessionNumbers,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ProcessedFilingRepository>();

        return await LoadProcessedSet(repo, accessionNumbers, cancellationToken);
    }

    internal async Task<bool> RecordProcessed(
        IReadOnlyCollection<string> accessionNumbers,
        CancellationToken cancellationToken,
        RealtimeSweepState sweepState = null
    )
    {
        if (accessionNumbers.Count == 0)
            return true;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ProcessedFilingRepository>();

        await using var transaction =
            sweepState == null
                ? null
                : await repo.CreateTransaction(IsolationLevel.ReadCommitted, cancellationToken);
        if (sweepState != null)
        {
            // Serialize only the completion write with identity resets, never the SEC request.
            var states = scope.ServiceProvider.GetRequiredService<RealtimeSweepStateRepository>();
            var current = await states.Lock(sweepState.Id, cancellationToken);
            if (
                current == null
                || current.UpdatedAt != sweepState.UpdatedAt
                || current.SweptThrough != sweepState.SweptThrough
            )
                return false;
        }

        // Re-check inside the write scope: a parallel sweep (or the quarterly
        // worker) may have recorded some of these between discovery and now.
        var existingSet = await LoadProcessedSet(repo, accessionNumbers, cancellationToken);

        foreach (var accession in accessionNumbers)
        {
            if (existingSet.Add(accession))
                repo.Add(new ProcessedFiling { AccessionNumber = accession });
        }

        await repo.SaveChanges();
        if (transaction != null)
            await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task<HashSet<string>> LoadProcessedSet(
        ProcessedFilingRepository repo,
        IEnumerable<string> accessionNumbers,
        CancellationToken cancellationToken
    )
    {
        var processed = await repo.GetByAccessionNumbers(accessionNumbers.ToList())
            .Select(p => p.AccessionNumber)
            .ToListAsync(cancellationToken);

        return new HashSet<string>(processed, StringComparer.OrdinalIgnoreCase);
    }

    internal async Task<Parsed13FFiling> ParseFiling(
        EdgarDailyIndexEntry entry,
        CancellationToken cancellationToken
    )
    {
        var submission = await TryReadSubmission(entry, cancellationToken);
        if (submission?.Filing != null)
            return submission.Filing;

        var artifacts =
            submission?.ArtifactNames
            ?? await _edgarClient.GetFilingArtifactNames(
                entry.Cik,
                entry.AccessionNumber,
                cancellationToken
            );

        var primaryDocName = SelectCoverPage(artifacts);
        if (primaryDocName == null)
        {
            _logger.LogWarning(
                "Filing {Accession} has no primary_doc.xml, skipping",
                entry.AccessionNumber
            );
            return null;
        }

        var primaryDocXml = await DownloadText(
            entry.Cik,
            entry.AccessionNumber,
            primaryDocName,
            cancellationToken
        );
        if (string.IsNullOrWhiteSpace(primaryDocXml))
            return null;

        var filing = _parser.ParseCoverPage(
            primaryDocXml,
            entry.AccessionNumber,
            entry.Cik,
            entry.DateFiled
        );

        filing.Holdings = await ResolveHoldings(entry, artifacts, cancellationToken);

        // A non-amendment with no parseable holdings means we picked the wrong
        // artifact or the table failed to parse — never feed an empty original
        // through (it would be a no-op at best, masking a real gap). Skip it and
        // let the authoritative quarterly bulk import reconcile it later.
        if (filing.Holdings.Count == 0 && !filing.IsAmendment)
        {
            if (submission?.OriginalFallback != null)
            {
                _logger.LogInformation(
                    "Using complete-submission positions for original 13F {Accession}; standalone table unavailable and cover totals unreconciled",
                    entry.AccessionNumber
                );
                return submission.OriginalFallback;
            }
            _logger.LogWarning(
                "Filing {Accession} yielded no holdings and is not an amendment — skipping",
                entry.AccessionNumber
            );
            return null;
        }

        return filing;
    }

    // Complete submissions stay available when the artifact directory returns server errors.
    // Bound this first route so unavailable text never starves the existing XML-artifact route.
    private async Task<Parsed13FSubmission> TryReadSubmission(
        EdgarDailyIndexEntry entry,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var content = await _edgarClient.GetDocumentContent(
                entry.AccessionNumber,
                entry.Cik,
                timeout.Token
            );
            cancellationToken.ThrowIfCancellationRequested();
            return Filing13FSubmissionParser.Parse(content, entry, _parser);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Complete 13F submission timed out for {Accession}; trying artifacts",
                entry.AccessionNumber
            );
            return null;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or XmlException or FormatException)
        {
            _logger.LogInformation(
                exception,
                "Complete 13F submission unavailable for {Accession}; trying artifacts",
                entry.AccessionNumber
            );
            return null;
        }
    }

    /// <summary>
    /// Downloads candidate information-table XML artifacts and returns the
    /// first that actually parses to one or more <c>&lt;infoTable&gt;</c> rows.
    /// An empty result is only legitimate for a holdings-removing amendment —
    /// the caller enforces that.
    /// </summary>
    private async Task<List<Parsed13FHolding>> ResolveHoldings(
        EdgarDailyIndexEntry entry,
        List<string> artifacts,
        CancellationToken cancellationToken
    )
    {
        foreach (var name in OrderedInfoTableCandidates(artifacts))
        {
            var xml = await DownloadText(entry.Cik, entry.AccessionNumber, name, cancellationToken);
            if (string.IsNullOrWhiteSpace(xml))
                continue;

            var holdings = _parser.ParseInformationTable(xml);
            if (holdings.Count > 0)
                return holdings;
        }

        return [];
    }

    private async Task<string> DownloadText(
        string cik,
        string accessionNumber,
        string fileName,
        CancellationToken cancellationToken
    )
    {
        var bytes = await _edgarClient.GetDocumentFileBytes(
            cik,
            accessionNumber,
            fileName,
            cancellationToken
        );
        return bytes.Length == 0 ? null : Encoding.UTF8.GetString(bytes);
    }

    private static string SelectCoverPage(List<string> artifacts) =>
        artifacts.FirstOrDefault(a =>
            a.Equals("primary_doc.xml", StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>
    /// The information table is one of the filing's non-cover <c>.xml</c>
    /// artifacts; SEC names it inconsistently (<c>infotable.xml</c>,
    /// <c>form13fInfoTable.xml</c>, <c>&lt;accession&gt;.xml</c>) and filings
    /// can also ship rendering/schema XML. Yield the table-looking names first,
    /// then the rest, so the caller can validate by content rather than trust
    /// a single name guess.
    /// </summary>
    private static IEnumerable<string> OrderedInfoTableCandidates(List<string> artifacts)
    {
        var candidates = artifacts
            .Where(a =>
                a.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                && !a.Equals("primary_doc.xml", StringComparison.OrdinalIgnoreCase)
            )
            .ToList();

        return candidates
            .OrderByDescending(a =>
                a.Contains("info", StringComparison.OrdinalIgnoreCase)
                || a.Contains("table", StringComparison.OrdinalIgnoreCase)
                || a.Contains("13f", StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
    }
}
