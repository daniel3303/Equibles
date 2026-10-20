using System.Net;
using System.Text;
using System.Xml;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Extensions;
using Equibles.Holdings.HostedService.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Makes every machine-readable 13D/13G on EDGAR reach the holdings tables once its security is
/// tracked. The realtime sweep only looks back a few days, so a filing it missed (a failed import,
/// a fix deployed later, an issuer seeded after the window) stays missing without this pass.
/// </summary>
[Service]
public class Schedule13DGCoverageService
{
    private const string PrimaryDocument = "primary_doc.xml";

    private readonly ISecEdgarClient _edgarClient;
    private readonly Filing13DGXmlParser _parser;
    private readonly Realtime13DGIngestionService _ingestion;
    private readonly BeneficialOwnershipDetailWriter _detailWriter;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Schedule13DGCoverageService> _logger;

    public Schedule13DGCoverageService(
        ISecEdgarClient edgarClient,
        Filing13DGXmlParser parser,
        Realtime13DGIngestionService ingestion,
        BeneficialOwnershipDetailWriter detailWriter,
        IServiceScopeFactory scopeFactory,
        ILogger<Schedule13DGCoverageService> logger
    )
    {
        _edgarClient = edgarClient;
        _parser = parser;
        _ingestion = ingestion;
        _detailWriter = detailWriter;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Walks the daily indexes from <paramref name="from"/> through <paramref name="through"/>,
    /// oldest first, reading every 13D/13G accession that has neither stored holdings nor stored
    /// details. Stops after the day on which <paramref name="maxDocuments"/> documents were read,
    /// and before any day it could not finish because EDGAR was unavailable.
    /// </summary>
    public async Task<Schedule13DGBackfillResult> BackfillDays(
        DateOnly from,
        DateOnly through,
        DateOnly minReportDate,
        int maxDocuments,
        CancellationToken cancellationToken
    )
    {
        DateOnly? completedThrough = null;
        var documents = 0;
        var imported = 0;
        for (var day = from; day <= through; day = day.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dayResult = await BackfillDay(day, minReportDate, cancellationToken);
            documents += dayResult.Documents;
            imported += dayResult.Imported;
            if (!dayResult.Completed)
                return new Schedule13DGBackfillResult(completedThrough, documents, imported, true);

            completedThrough = day;
            if (documents >= maxDocuments)
                break;
        }

        return new Schedule13DGBackfillResult(completedThrough, documents, imported, false);
    }

    /// <summary>
    /// Re-reads up to <paramref name="maxFilings"/> filings whose details are stored without a
    /// position although their CUSIP now resolves to a tracked security, oldest first, so an
    /// issuer seeded after its filings were read still gets them. A filing is retried at most
    /// once per <paramref name="backoff"/>, because every read refreshes its details.
    /// </summary>
    public async Task<int> RetryResolvable(
        int maxFilings,
        TimeSpan backoff,
        DateOnly minReportDate,
        CancellationToken cancellationToken
    )
    {
        var candidates = await LoadResolvableCandidates(
            backoff,
            minReportDate,
            maxFilings,
            cancellationToken
        );

        var imported = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsSuperseded(candidate, cancellationToken))
                continue;

            var read = await ReadDocument(
                candidate.FilerCik ?? candidate.IssuerCik,
                candidate.AccessionNumber,
                candidate.FilingDate,
                cancellationToken
            );
            if (read.Status == Schedule13DGReadStatus.Outage)
            {
                _logger.LogWarning(
                    "13D/13G coverage retry stopped: EDGAR is unavailable after {Imported} imports",
                    imported
                );
                break;
            }

            if (read.Status != Schedule13DGReadStatus.Parsed)
            {
                // The stored details came from an earlier read and stay; the backoff restarts.
                await TouchReadAt(candidate.AccessionNumber, cancellationToken);
                continue;
            }

            if (Realtime13DGIngestionService.RejectionReason(read.Filing) != null)
            {
                await _detailWriter.TryWrite(read.Filing, cancellationToken);
                continue;
            }

            var outcome = await _ingestion.ImportParsedFiling(
                read.Filing,
                minReportDate,
                cancellationToken
            );
            if (outcome == Schedule13DGImportOutcome.Imported)
                imported++;
        }

        if (candidates.Count > 0)
            _logger.LogInformation(
                "13D/13G coverage retry imported {Imported} of {Candidates} filings whose CUSIP now resolves",
                imported,
                candidates.Count
            );
        return imported;
    }

    private async Task<Schedule13DGBackfillDay> BackfillDay(
        DateOnly day,
        DateOnly minReportDate,
        CancellationToken cancellationToken
    )
    {
        List<EdgarDailyIndexEntry> indexEntries;
        try
        {
            indexEntries = await _edgarClient.GetDailyIndexForForms(
                day,
                Realtime13DGIngestionService.ScheduleFormPrefixes,
                cancellationToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "13D/13G coverage could not read the {Day:yyyy-MM-dd} daily index; stopping",
                day
            );
            return new Schedule13DGBackfillDay(false, 0, 0);
        }

        // A filing is listed once under its filer and once under its subject company.
        var entries = indexEntries
            .Where(entry => !string.IsNullOrEmpty(entry.AccessionNumber))
            .GroupBy(entry => entry.AccessionNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var known = await LoadKnownAccessions(
            entries.Select(entry => entry.AccessionNumber).ToList(),
            cancellationToken
        );

        var documents = 0;
        var imported = 0;
        foreach (
            var entry in entries
                .Where(entry => !known.Contains(entry.AccessionNumber))
                .OrderBy(entry => entry.AccessionNumber, StringComparer.Ordinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await ReadDocument(
                entry.Cik,
                entry.AccessionNumber,
                entry.DateFiled,
                cancellationToken
            );
            if (read.Status == Schedule13DGReadStatus.Outage)
            {
                _logger.LogWarning(
                    "13D/13G coverage stopped on {Day:yyyy-MM-dd}: EDGAR is unavailable",
                    day
                );
                return new Schedule13DGBackfillDay(false, documents, imported);
            }

            documents++;
            if (read.Status == Schedule13DGReadStatus.Failed)
                continue;
            if (read.Status == Schedule13DGReadStatus.NotServed)
            {
                // A document EDGAR does not serve never changes; recording it stops a re-read.
                await _detailWriter.WriteUnreadable(
                    entry.AccessionNumber,
                    entry.FormType.ToHoldingsFilingType() ?? FilingType.Schedule13D,
                    entry.DateFiled,
                    entry.Cik,
                    cancellationToken
                );
                continue;
            }

            if (
                Realtime13DGIngestionService.RejectionReason(read.Filing) != null
                || read.Filing.DateOfEvent < minReportDate
            )
            {
                await _detailWriter.TryWrite(read.Filing, cancellationToken);
                continue;
            }

            // Details are written before the import, so a filing whose import fails or whose
            // security is untracked stays on file for RetryResolvable.
            var outcome = await _ingestion.ImportParsedFiling(
                read.Filing,
                minReportDate,
                cancellationToken
            );
            if (outcome == Schedule13DGImportOutcome.Imported)
                imported++;
        }

        if (documents > 0)
            _logger.LogInformation(
                "13D/13G coverage read {Documents} filings from {Day:yyyy-MM-dd} and imported {Imported}",
                documents,
                day,
                imported
            );
        return new Schedule13DGBackfillDay(true, documents, imported);
    }

    private async Task<Schedule13DGDocumentRead> ReadDocument(
        string cik,
        string accessionNumber,
        DateOnly filingDate,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(cik))
            return new Schedule13DGDocumentRead(null, Schedule13DGReadStatus.Failed);

        byte[] bytes;
        try
        {
            bytes = await _edgarClient.GetDocumentFileBytes(
                cik,
                accessionNumber,
                PrimaryDocument,
                cancellationToken
            );
        }
        catch (HttpRequestException exception) when (!IsOutage(exception))
        {
            // Only a document EDGAR says it does not have is final; any other refusal may pass.
            _logger.LogInformation(
                "EDGAR refused 13D/13G {Accession} ({Status})",
                accessionNumber,
                exception.StatusCode
            );
            return new Schedule13DGDocumentRead(
                null,
                exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone
                    ? Schedule13DGReadStatus.NotServed
                    : Schedule13DGReadStatus.Failed
            );
        }
        catch (Exception exception)
            when (exception is HttpRequestException
                || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested
            )
        {
            _logger.LogWarning(exception, "13D/13G {Accession} could not be read", accessionNumber);
            return new Schedule13DGDocumentRead(null, Schedule13DGReadStatus.Outage);
        }

        if (bytes == null || bytes.Length == 0)
            return new Schedule13DGDocumentRead(null, Schedule13DGReadStatus.NotServed);

        try
        {
            return new Schedule13DGDocumentRead(
                _parser.ParseFiling(
                    Encoding.UTF8.GetString(bytes),
                    accessionNumber,
                    cik,
                    filingDate
                ),
                Schedule13DGReadStatus.Parsed
            );
        }
        catch (Exception exception) when (exception is XmlException or FormatException)
        {
            // Left retryable: a parser fix ships with a CoverageVersion bump, whose walk reads
            // every accession with nothing on file again.
            _logger.LogWarning(exception, "13D/13G {Accession} cannot be parsed", accessionNumber);
            return new Schedule13DGDocumentRead(null, Schedule13DGReadStatus.Failed);
        }
    }

    private async Task TouchReadAt(string accessionNumber, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var now = DateTime.UtcNow;
        await dbContext
            .Set<BeneficialOwnershipFiling>()
            .Where(filing => filing.AccessionNumber == accessionNumber)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(filing => filing.ReadAt, now),
                cancellationToken
            );
    }

    // Accessions already imported, already read, or recorded as processed by the realtime sweep.
    private async Task<HashSet<string>> LoadKnownAccessions(
        List<string> accessions,
        CancellationToken cancellationToken
    )
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (accessions.Count == 0)
            return known;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        known.UnionWith(
            await dbContext
                .Set<BeneficialOwnershipFiling>()
                .Where(filing => accessions.Contains(filing.AccessionNumber))
                .Select(filing => filing.AccessionNumber)
                .ToListAsync(cancellationToken)
        );
        known.UnionWith(
            await dbContext
                .Set<ProcessedFiling>()
                .Where(filing => accessions.Contains(filing.AccessionNumber))
                .Select(filing => filing.AccessionNumber)
                .ToListAsync(cancellationToken)
        );
        known.UnionWith(
            await dbContext
                .Set<InstitutionalHolding>()
                .Where(holding => accessions.Contains(holding.AccessionNumber))
                .Select(holding => holding.AccessionNumber)
                .Distinct()
                .ToListAsync(cancellationToken)
        );
        return known;
    }

    private async Task<List<Schedule13DGRetryCandidate>> LoadResolvableCandidates(
        TimeSpan backoff,
        DateOnly minReportDate,
        int maxFilings,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var readBefore = DateTime.UtcNow - backoff;
        var unstored = dbContext
            .Set<BeneficialOwnershipFiling>()
            .AsNoTracking()
            .Where(filing =>
                !filing.IsUnreadable
                && filing.FilingType != FilingType.Form13F
                && filing.IssuerCusip != null
                && filing.DateOfEvent >= minReportDate
                && filing.ReadAt < readBefore
                && filing.ReportingPersons.Any()
                && !dbContext
                    .Set<InstitutionalHolding>()
                    .Any(holding => holding.AccessionNumber == filing.AccessionNumber)
                && !dbContext
                    .Set<ProcessedFiling>()
                    .Any(processed => processed.AccessionNumber == filing.AccessionNumber)
            );

        var cusips = await unstored
            .Select(filing => filing.IssuerCusip.ToUpper())
            .Distinct()
            .ToListAsync(cancellationToken);
        if (cusips.Count == 0)
            return [];

        var issuers = scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();
        var claims = await HoldingCusipResolution.Load(
            issuers,
            issuers.GetAll(),
            cusips,
            cancellationToken
        );
        var resolvable = HoldingCusipResolution.Resolve(claims, []).Keys.ToList();
        if (resolvable.Count == 0)
            return [];

        // Chronological, so an original is retried before its amendment.
        return await unstored
            .Where(filing => resolvable.Contains(filing.IssuerCusip.ToUpper()))
            .OrderBy(filing => filing.FilingDate)
            .ThenBy(filing => filing.AccessionNumber)
            .Select(filing => new Schedule13DGRetryCandidate(
                filing.AccessionNumber,
                filing.FilingType,
                filing.IsAmendment,
                filing.FilingDate,
                filing.DateOfEvent.Value,
                filing.FilerCik,
                filing.IssuerCik,
                filing.IssuerCusip
            ))
            .Take(maxFilings)
            .ToListAsync(cancellationToken);
    }

    private async Task<bool> IsSuperseded(
        Schedule13DGRetryCandidate candidate,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(candidate.FilerCik))
            return false;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        return await Schedule13DGSupersession
            .LaterStoredFilings(
                dbContext,
                candidate.FilerCik,
                candidate.IssuerCusip,
                candidate.FilingType,
                candidate.DateOfEvent,
                candidate.FilingDate,
                candidate.IsAmendment,
                candidate.AccessionNumber
            )
            .AnyAsync(cancellationToken);
    }

    // A missing status, a server error, a throttle or a 403 (SEC's rate-limit answer) is an outage.
    private static bool IsOutage(HttpRequestException exception) =>
        exception.StatusCode
            is null
                or >= HttpStatusCode.InternalServerError
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.Forbidden;
}
