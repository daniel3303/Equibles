using System.Text;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
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
    public async Task<int> IngestRecentFilings(
        DateOnly today,
        int lookbackDays,
        DateOnly minReportDate,
        CancellationToken cancellationToken
    )
    {
        var entries = await DiscoverEntries(today, lookbackDays, cancellationToken);
        if (entries.Count == 0)
        {
            _logger.LogInformation("No 13F-HR submissions found in daily index window");
            return 0;
        }

        var filings = new List<Parsed13FFiling>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (await AlreadyImported(entry.AccessionNumber, cancellationToken))
                {
                    _logger.LogDebug(
                        "Skipping already-imported filing {Accession}",
                        entry.AccessionNumber
                    );
                    continue;
                }

                var filing = await ParseFiling(entry, cancellationToken);
                if (filing == null)
                    continue;

                if (filing.PeriodOfReport == DateOnly.MinValue)
                {
                    _logger.LogWarning(
                        "Skipping filing {Accession}: unparseable report period",
                        entry.AccessionNumber
                    );
                    continue;
                }

                if (filing.PeriodOfReport < minReportDate)
                    continue;

                filings.Add(filing);
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
            }
        }

        if (filings.Count == 0)
        {
            _logger.LogInformation("No new 13F-HR filings to import after filtering");
            return 0;
        }

        _logger.LogInformation(
            "Importing {Count} newly filed 13F-HR submissions via real-time path",
            filings.Count
        );

        using var archive = _archiveBuilder.Build(filings);
        await _importService.ImportDataSet(archive, minReportDate, cancellationToken);

        return filings.Count;
    }

    private async Task<List<Equibles.Integrations.Sec.Models.EdgarDailyIndexEntry>> DiscoverEntries(
        DateOnly today,
        int lookbackDays,
        CancellationToken cancellationToken
    )
    {
        // Deduplicate by accession across the window: an amendment carries a
        // distinct accession number, so this only collapses the same filing
        // re-listed across overlapping sweeps, never an original vs amendment.
        var byAccession = new Dictionary<string, Equibles.Integrations.Sec.Models.EdgarDailyIndexEntry>(
            StringComparer.OrdinalIgnoreCase
        );

        for (var offset = 0; offset < lookbackDays; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var date = today.AddDays(-offset);
            var indexEntries = await _edgarClient.GetDailyIndex(date, cancellationToken);

            foreach (var entry in indexEntries)
            {
                if (
                    !string.IsNullOrEmpty(entry.AccessionNumber)
                    && entry.FormType.StartsWith("13F-HR", StringComparison.OrdinalIgnoreCase)
                )
                {
                    byAccession[entry.AccessionNumber] = entry;
                }
            }
        }

        return byAccession.Values.ToList();
    }

    private async Task<bool> AlreadyImported(
        string accessionNumber,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<InstitutionalHoldingRepository>();
        return await repo.GetByAccessionNumber(accessionNumber).AnyAsync(cancellationToken);
    }

    private async Task<Parsed13FFiling> ParseFiling(
        Equibles.Integrations.Sec.Models.EdgarDailyIndexEntry entry,
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

        var infoTableName = SelectInfoTable(artifacts);
        if (infoTableName != null)
        {
            var infoTableXml = await DownloadText(
                entry.Cik,
                entry.AccessionNumber,
                infoTableName,
                cancellationToken
            );
            filing.Holdings = _parser.ParseInformationTable(infoTableXml);
        }
        else
        {
            // No information table is legitimate for an amendment that removes
            // every position; the pipeline's amendment delete-by-period still runs.
            _logger.LogInformation(
                "Filing {Accession} has no information table (likely a holdings-removing amendment)",
                entry.AccessionNumber
            );
        }

        return filing;
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
    /// The information table is the filing's other <c>.xml</c> artifact. SEC
    /// names it inconsistently (<c>infotable.xml</c>, <c>form13fInfoTable.xml</c>,
    /// <c>&lt;accession&gt;.xml</c>), so prefer a name that looks like an info
    /// table and fall back to any non-cover, non-schema XML.
    /// </summary>
    private static string SelectInfoTable(List<string> artifacts)
    {
        var candidates = artifacts
            .Where(a =>
                a.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                && !a.Equals("primary_doc.xml", StringComparison.OrdinalIgnoreCase)
            )
            .ToList();

        return candidates.FirstOrDefault(a =>
                a.Contains("info", StringComparison.OrdinalIgnoreCase)
                || a.Contains("table", StringComparison.OrdinalIgnoreCase)
                || a.Contains("13f", StringComparison.OrdinalIgnoreCase)
            ) ?? candidates.FirstOrDefault();
    }
}
