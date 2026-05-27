using System.Text;
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
        CancellationToken cancellationToken
    )
    {
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

        // Sort chronologically so originals are always imported before their
        // amendments — HandleAmendments in the import pipeline deletes prior
        // holdings for the same holder+period before inserting the amendment.
        var sorted = entries
            .OrderBy(e => e.DateFiled)
            .ThenBy(e => e.AccessionNumber, StringComparer.Ordinal)
            .ToList();

        var totalImported = 0;
        foreach (var entry in sorted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (alreadyProcessed.Contains(entry.AccessionNumber))
            {
                _logger.LogDebug(
                    "Skipping already-processed filing {Accession}",
                    entry.AccessionNumber
                );
                continue;
            }

            var filing = await TryParseAndValidateEntry(entry, minReportDate, cancellationToken);
            if (filing == null)
                continue;

            _logger.LogInformation(
                "Importing 13F-HR {Accession} (CIK {Cik}, period {Period})",
                entry.AccessionNumber,
                entry.Cik,
                filing.PeriodOfReport
            );

            using (var archive = _archiveBuilder.Build([filing]))
            {
                await _importService.ImportDataSet(archive, minReportDate, cancellationToken);
            }

            await RecordProcessed([entry.AccessionNumber], cancellationToken);
            totalImported++;
        }

        _logger.LogInformation(
            "13F real-time ingestion cycle complete: {Count} filings imported",
            totalImported
        );

        return new RealtimeIngestionResult(totalImported, earliestFailedDate);
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

            if (filing.PeriodOfReport < minReportDate)
                return null;

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

    private async Task RecordProcessed(
        IReadOnlyCollection<string> accessionNumbers,
        CancellationToken cancellationToken
    )
    {
        if (accessionNumbers.Count == 0)
            return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ProcessedFilingRepository>();

        // Re-check inside the write scope: a parallel sweep (or the quarterly
        // worker) may have recorded some of these between discovery and now.
        var existingSet = await LoadProcessedSet(repo, accessionNumbers, cancellationToken);

        foreach (var accession in accessionNumbers)
        {
            if (existingSet.Add(accession))
                repo.Add(new ProcessedFiling { AccessionNumber = accession });
        }

        await repo.SaveChanges();
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

    private async Task<Parsed13FFiling> ParseFiling(
        EdgarDailyIndexEntry entry,
        CancellationToken cancellationToken
    )
    {
        var artifacts = await _edgarClient.GetFilingArtifactNames(
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
            _logger.LogWarning(
                "Filing {Accession} yielded no holdings and is not an amendment — skipping",
                entry.AccessionNumber
            );
            return null;
        }

        return filing;
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
