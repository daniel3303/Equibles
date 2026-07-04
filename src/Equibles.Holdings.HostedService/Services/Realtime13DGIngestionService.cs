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
/// Near-real-time Schedule 13D/13G ingestion. Discovers freshly accepted
/// beneficial-ownership filings from EDGAR's daily index, parses their XML
/// <c>primary_doc.xml</c>, and feeds them through the existing bulk-dataset
/// import pipeline. Unlike 13F there is no quarterly authoritative data set, so
/// this is the sole source — coverage begins 2024-12-18 when the forms became
/// machine-readable XML.
/// </summary>
[Service]
public class Realtime13DGIngestionService
{
    // Daily-index form types: "SCHEDULE 13D", "SCHEDULE 13D/A", "SCHEDULE 13G",
    // "SCHEDULE 13G/A" — all matched by these StartsWith prefixes.
    private static readonly string[] ScheduleFormPrefixes = ["SCHEDULE 13D", "SCHEDULE 13G"];

    private readonly ISecEdgarClient _edgarClient;
    private readonly Filing13DGXmlParser _parser;
    private readonly Realtime13DGArchiveBuilder _archiveBuilder;
    private readonly HoldingsImportService _importService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Realtime13DGIngestionService> _logger;

    public Realtime13DGIngestionService(
        ISecEdgarClient edgarClient,
        Filing13DGXmlParser parser,
        Realtime13DGArchiveBuilder archiveBuilder,
        HoldingsImportService importService,
        IServiceScopeFactory scopeFactory,
        ILogger<Realtime13DGIngestionService> logger
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
    /// Sweeps the last <paramref name="lookbackDays"/> days of the daily index
    /// (inclusive of <paramref name="today"/>), ingesting every new 13D/13G
    /// whose event date is on/after <paramref name="minReportDate"/>.
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
            _logger.LogInformation("No 13D/13G submissions found in daily index window");
            return new RealtimeIngestionResult(0, earliestFailedDate);
        }

        var alreadyProcessed = await LoadProcessedAccessions(
            entries.Select(e => e.AccessionNumber),
            cancellationToken
        );

        // Sort chronologically so originals import before their amendments.
        var sorted = entries
            .OrderBy(e => e.DateFiled)
            .ThenBy(e => e.AccessionNumber, StringComparer.Ordinal)
            .ToList();

        // Seeded with the discovery-phase failure; per-filing import failures
        // below pull it further back so the watermark re-covers them too.
        var earliestRetryDate = earliestFailedDate;

        var totalImported = 0;
        foreach (var entry in sorted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (alreadyProcessed.Contains(entry.AccessionNumber))
                continue;

            var filing = await TryParseAndValidateEntry(entry, minReportDate, cancellationToken);
            if (filing == null)
                continue;

            _logger.LogInformation(
                "Importing {Form} {Accession} (CIK {Cik}, event {Event})",
                filing.SubmissionType,
                entry.AccessionNumber,
                entry.Cik,
                filing.DateOfEvent
            );

            ImportResult importResult;
            try
            {
                using var archive = _archiveBuilder.Build([filing]);
                importResult = await _importService.ImportDataSet(
                    archive,
                    minReportDate,
                    cancellationToken
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A poisoned filing must cost only its own rows, never the sweep:
                // skip it and leave it unrecorded so a later cycle retries it
                // (e.g. once a fix for its defect deploys). Mirrors the per-entry
                // isolation TryParseAndValidateEntry gives parse failures. Holding
                // the watermark back keeps the filing re-discoverable even after
                // it ages out of the trailing window (EquiblesCommercial#2850).
                _logger.LogError(
                    ex,
                    "Failed to import {Form} {Accession} (CIK {Cik}); skipping filing",
                    filing.SubmissionType,
                    entry.AccessionNumber,
                    entry.Cik
                );
                if (earliestRetryDate == null || entry.DateFiled < earliestRetryDate)
                    earliestRetryDate = entry.DateFiled;
                continue;
            }

            // IsComplete=false is the import service's "retry later" contract (e.g.
            // NoTrackedStocks until CUSIPs seed) — recording it here would consume
            // the filing forever (EquiblesCommercial#2850). It deliberately does
            // NOT hold the watermark back: an issuer that never seeds a CUSIP
            // would wedge the sweep, so its retry is bounded by the trailing
            // window instead.
            if (!importResult.IsComplete)
                continue;

            // A filing that imported "complete" yet inserted zero holdings is
            // suspect: recording it would consume the accession forever even
            // though we hold none of its position (e.g. a blank filer CIK made
            // the holder upsert skip it). Unlike 13F there is no quarterly bulk
            // data set to reconcile the loss, so retry instead — and every
            // 13D/G archive row carries the issuer position (an amendment
            // re-reports it, a final exit reports zero shares), so zero inserts
            // is never legitimate here, amendments included.
            if (importResult.InsertedHoldings == 0)
            {
                _logger.LogWarning(
                    "{Form} {Accession} (CIK {Cik}) imported as complete but inserted no holdings; not recording so a later cycle retries",
                    filing.SubmissionType,
                    entry.AccessionNumber,
                    entry.Cik
                );
                if (earliestRetryDate == null || entry.DateFiled < earliestRetryDate)
                    earliestRetryDate = entry.DateFiled;
                continue;
            }

            await RecordProcessed([entry.AccessionNumber], cancellationToken);
            totalImported++;
        }

        _logger.LogInformation(
            "13D/13G real-time ingestion cycle complete: {Count} filings imported",
            totalImported
        );

        return new RealtimeIngestionResult(totalImported, earliestRetryDate);
    }

    private async Task<Parsed13DGFiling> TryParseAndValidateEntry(
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

            if (filing.DateOfEvent == DateOnly.MinValue)
            {
                _logger.LogWarning(
                    "Skipping filing {Accession}: unparseable event date",
                    entry.AccessionNumber
                );
                return null;
            }

            if (filing.DateOfEvent < minReportDate)
                return null;

            if (filing.ReportingPersons.Count == 0)
            {
                _logger.LogWarning(
                    "Skipping filing {Accession}: no reporting persons",
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
            // One malformed filing must not abort the whole sweep.
            _logger.LogError(
                ex,
                "Failed to ingest 13D/13G filing {Accession} (CIK {Cik}), continuing",
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
        var byAccession = new Dictionary<string, EdgarDailyIndexEntry>(
            StringComparer.OrdinalIgnoreCase
        );

        DateOnly? earliestFailed = null;

        for (var offset = 0; offset < lookbackDays; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var date = today.AddDays(-offset);

            List<EdgarDailyIndexEntry> indexEntries;
            try
            {
                indexEntries = await _edgarClient.GetDailyIndexForForms(
                    date,
                    ScheduleFormPrefixes,
                    cancellationToken
                );
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

            foreach (var entry in indexEntries)
            {
                if (!string.IsNullOrEmpty(entry.AccessionNumber))
                    byAccession[entry.AccessionNumber] = entry;
            }
        }

        return (byAccession.Values.ToList(), earliestFailed);
    }

    private async Task<Parsed13DGFiling> ParseFiling(
        EdgarDailyIndexEntry entry,
        CancellationToken cancellationToken
    )
    {
        var artifacts = await _edgarClient.GetFilingArtifactNames(
            entry.Cik,
            entry.AccessionNumber,
            cancellationToken
        );

        var primaryDocName = artifacts.FirstOrDefault(a =>
            a.Equals("primary_doc.xml", StringComparison.OrdinalIgnoreCase)
        );
        if (primaryDocName == null)
        {
            _logger.LogWarning(
                "Filing {Accession} has no primary_doc.xml, skipping",
                entry.AccessionNumber
            );
            return null;
        }

        var bytes = await _edgarClient.GetDocumentFileBytes(
            entry.Cik,
            entry.AccessionNumber,
            primaryDocName,
            cancellationToken
        );
        if (bytes.Length == 0)
            return null;

        return _parser.ParseFiling(
            Encoding.UTF8.GetString(bytes),
            entry.AccessionNumber,
            entry.Cik,
            entry.DateFiled
        );
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
}
