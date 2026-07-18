using System.Text;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Extensions;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Equibles.Sec.HostedService;

public class DocumentScraper : IDocumentScraper
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ICompanySyncService _companySyncService;
    private readonly IFilingDiscoveryService _filingDiscoveryService;
    private readonly IEnumerable<IFilingProcessor> _filingProcessors;
    private readonly DocumentScraperOptions _options;
    private readonly WorkerOptions _workerOptions;
    private readonly ILogger<DocumentScraper> _logger;
    private readonly ErrorReporter _errorReporter;
    private readonly ResiliencePipeline _retryPipeline;

    // Annual foreign-filer forms tried (in order) when SEC metadata and 10-Ks are both
    // absent. 20-F first, then 40-F — Canadian cross-listed companies file 40-F instead.
    private static readonly (
        DocumentTypeFilter Filter,
        string FormName
    )[] ForeignFilerAnnualForms =
    [
        (DocumentTypeFilter.TwentyF, "20-F"),
        (DocumentTypeFilter.FortyF, "40-F"),
    ];

    // When the company directory was last synced from SEC. Static because the
    // scraper is resolved per cycle and event-driven cycles run every few
    // seconds — an unthrottled sync would re-fetch company_tickers each time.
    private static DateTime _lastCompanySyncAtUtc;

    public DocumentScraper(
        IServiceScopeFactory serviceScopeFactory,
        ICompanySyncService companySyncService,
        IFilingDiscoveryService filingDiscoveryService,
        IEnumerable<IFilingProcessor> filingProcessors,
        IOptions<DocumentScraperOptions> options,
        IOptions<WorkerOptions> workerOptions,
        ILogger<DocumentScraper> logger,
        ErrorReporter errorReporter
    )
    {
        _serviceScopeFactory = serviceScopeFactory;
        _companySyncService = companySyncService;
        _filingDiscoveryService = filingDiscoveryService;
        _filingProcessors = filingProcessors;
        _options = options.Value;
        _workerOptions = workerOptions.Value;
        _logger = logger;
        _errorReporter = errorReporter;
        _retryPipeline = BuildRetryPipeline();
    }

    public async Task<ScrapingResult> ScrapeDocuments(CancellationToken cancellationToken = default)
    {
        var result = new ScrapingResult();
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogInformation("Starting document scraping process...");

            if (ShouldSyncCompanies())
            {
                await _companySyncService.SyncCompaniesFromSecApi();
                _lastCompanySyncAtUtc = DateTime.UtcNow;
            }

            var companiesUntracked = await GetAllCompaniesWithNoTracking();

            var targets = _options.UseEventDrivenDiscovery
                ? await SelectEventDrivenTargets(companiesUntracked, cancellationToken)
                : companiesUntracked;

            _logger.LogInformation(
                "Found {CompanyCount} companies to process for documents",
                targets.Count
            );

            foreach (var companyUntracked in targets)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await ProcessCompanyDocumentsWithScope(companyUntracked, result);

                if (_options.UseEventDrivenDiscovery)
                    await StampFilingSyncState(companyUntracked);

                result.CompaniesProcessed++;
            }

            if (result.DeferredFilings.Count > 0)
                await RetryDeferredFilings(result, cancellationToken);

            result.Duration = DateTime.UtcNow - startTime;

            _logger.LogInformation(
                "Document scraping completed. Processed: {CompaniesProcessed}, Found: {DocumentsFound}, Added: {DocumentsAdded}, Skipped: {DocumentsSkipped}, Errors: {Errors}",
                result.CompaniesProcessed,
                result.DocumentsFound,
                result.DocumentsAdded,
                result.DocumentsSkipped,
                result.Errors
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during document scraping process");
            RecordError(result, "General error", ex);
            await ReportError("ScrapeDocuments", ex);
        }

        return result;
    }

    // Test seam: the throttle stamp is static (it must outlive the per-cycle
    // scraper), so suites reset it to stay order-independent.
    internal static void ResetCompanySyncThrottleForTests() => _lastCompanySyncAtUtc = default;

    // Legacy mode syncs every cycle (one multi-hour full sweep per cycle keeps
    // that cheap); event-driven mode throttles to the configured interval.
    private bool ShouldSyncCompanies() =>
        !_options.UseEventDrivenDiscovery
        || DateTime.UtcNow - _lastCompanySyncAtUtc
            >= TimeSpan.FromMinutes(_options.CompanySyncIntervalMinutes);

    /// <summary>
    /// The cycle's work list under event-driven discovery: companies flagged by
    /// the real-time feeds, plus the reconciliation batch — never-synced
    /// companies (fresh onboarding, full historical backfill) and companies
    /// whose last enumeration went stale. Discovery targets come first so a
    /// fresh filing is never queued behind a long reconciliation batch.
    /// </summary>
    private async Task<List<CommonStock>> SelectEventDrivenTargets(
        List<CommonStock> companies,
        CancellationToken cancellationToken
    )
    {
        var discovered = await _filingDiscoveryService.DiscoverCompaniesWithNewFilings(
            companies,
            cancellationToken
        );

        var reconciliation = await SelectReconciliationBatch(
            companies,
            discovered,
            cancellationToken
        );

        if (discovered.Count > 0 || reconciliation.Count > 0)
        {
            _logger.LogInformation(
                "Event-driven discovery: {Discovered} companies with new filings, {Reconciliation} due for reconciliation",
                discovered.Count,
                reconciliation.Count
            );
        }

        return [.. discovered, .. reconciliation];
    }

    private async Task<List<CommonStock>> SelectReconciliationBatch(
        List<CommonStock> companies,
        List<CommonStock> alreadySelected,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var syncStateRepository =
            scope.ServiceProvider.GetRequiredService<CompanyFilingSyncStateRepository>();

        var lastSyncedByCompany = await syncStateRepository
            .GetAll()
            .AsNoTracking()
            .ToDictionaryAsync(s => s.CommonStockId, s => s.LastSyncedAt, cancellationToken);

        return SelectDueCompanies(
            companies,
            lastSyncedByCompany,
            alreadySelected.Select(c => c.Id).ToHashSet(),
            DateTime.UtcNow.AddHours(-_options.ReconciliationHours),
            _options.MaxReconciliationsPerCycle
        );
    }

    /// <summary>
    /// Companies due for a reconciliation sweep: never synced (no stamp) or
    /// stamped before the cutoff. Never-synced first, then stalest first, so a
    /// cold start drains as an ordered rolling backfill under the cap.
    /// </summary>
    internal static List<CommonStock> SelectDueCompanies(
        List<CommonStock> companies,
        Dictionary<Guid, DateTime> lastSyncedByCompany,
        HashSet<Guid> excludedIds,
        DateTime cutoff,
        int maxCompanies
    ) =>
        companies
            .Where(c => !excludedIds.Contains(c.Id))
            .Where(c =>
                !lastSyncedByCompany.TryGetValue(c.Id, out var lastSynced) || lastSynced < cutoff
            )
            .OrderBy(c =>
                lastSyncedByCompany.TryGetValue(c.Id, out var lastSynced)
                    ? lastSynced
                    : DateTime.MinValue
            )
            .Take(maxCompanies)
            .ToList();

    /// <summary>
    /// Records that this company's filings were fully enumerated now. Stamped
    /// even after a partial failure — the company is retried by the next
    /// reconciliation window (or the next discovery event) rather than every
    /// cycle, which bounds how much budget a persistently failing company burns.
    /// </summary>
    private async Task StampFilingSyncState(CommonStock company)
    {
        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var syncStateRepository =
                scope.ServiceProvider.GetRequiredService<CompanyFilingSyncStateRepository>();

            var state = await syncStateRepository
                .GetByCommonStockId(company.Id)
                .FirstOrDefaultAsync();

            if (state == null)
            {
                syncStateRepository.Add(
                    new CompanyFilingSyncState
                    {
                        CommonStockId = company.Id,
                        LastSyncedAt = DateTime.UtcNow,
                    }
                );
            }
            else
            {
                state.LastSyncedAt = DateTime.UtcNow;
            }

            await syncStateRepository.SaveChanges();
        }
        catch (Exception ex)
        {
            // A missed stamp only means the company re-enters the next
            // reconciliation batch (or vanished mid-cycle via company sync).
            _logger.LogWarning(
                ex,
                "Could not stamp filing sync state for {Ticker}",
                company.Ticker
            );
        }
    }

    private async Task<List<CommonStock>> GetAllCompaniesWithNoTracking()
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var commonStockRepository =
            scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        if (_workerOptions.TickersToSync?.Count > 0)
        {
            return await commonStockRepository
                .GetByTickers(_workerOptions.TickersToSync)
                .AsNoTracking()
                .ToListAsync();
        }

        return await commonStockRepository.GetAll().AsNoTracking().ToListAsync();
    }

    private async Task ProcessCompanyDocumentsWithScope(
        CommonStock companyUntracked,
        ScrapingResult result
    )
    {
        var startTime = DateTime.UtcNow;
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var companyRepository = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var secEdgarClient = scope.ServiceProvider.GetRequiredService<ISecEdgarClient>();
        var persistenceService =
            scope.ServiceProvider.GetRequiredService<IDocumentPersistenceService>();

        var commonStockManager = scope.ServiceProvider.GetRequiredService<CommonStockManager>();
        var documentRepository = scope.ServiceProvider.GetRequiredService<DocumentRepository>();

        var company = await companyRepository.Get(companyUntracked.Id);

        try
        {
            _logger.LogInformation(
                "Processing documents for company: {Ticker} - {Name}",
                company.Ticker,
                company.Name
            );

            // Capture SEC submissions metadata (fiscal year-end, SIC, entity type)
            // before fetching filings: the metadata call primes the SEC client's
            // submissions cache so the first GetCompanyFilings hits the same URL and
            // adds no extra request.
            await UpdateCompanyMetadata(
                company,
                secEdgarClient,
                commonStockManager,
                documentRepository
            );

            foreach (var documentType in _options.DocumentTypesToSync)
            {
                var secFilter = documentType.ToSecEdgarFilter();
                if (secFilter == null)
                {
                    _logger.LogWarning(
                        "No SEC Edgar filter mapping found for document type: {DocumentType}",
                        documentType
                    );
                    continue;
                }

                await ProcessDocumentTypeForCompany(
                    company,
                    documentType,
                    secFilter.Value,
                    result,
                    secEdgarClient,
                    persistenceService
                );
            }

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "Completed processing documents for {Ticker} in {Duration}. Found: {DocumentsFound}, Added: {DocumentsAdded}, Skipped: {DocumentsSkipped}, Errors: {Errors}",
                company.Ticker,
                duration,
                result.DocumentsFound,
                result.DocumentsAdded,
                result.DocumentsSkipped,
                result.Errors
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing documents for company {Ticker}", company.Ticker);
            RecordError(result, $"Company {company.Ticker}", ex);
            await ReportError("ProcessCompany", ex, $"ticker: {company.Ticker}");
        }
    }

    /// <summary>
    /// Reads SEC EDGAR's submissions metadata for the company and persists the
    /// fields it derives: the SIC code and entity type (which tell operating
    /// companies apart from pooled investment vehicles), and the fiscal year-end.
    /// Fallback chain when the metadata API has no fiscal year-end: most recent
    /// 10-K period-end → 20-F report date → 40-F report date. Best-effort: a
    /// failure is logged and reported but never blocks document scraping, since
    /// this metadata is a nice-to-have enrichment.
    /// </summary>
    private async Task UpdateCompanyMetadata(
        CommonStock company,
        ISecEdgarClient secEdgarClient,
        CommonStockManager commonStockManager,
        DocumentRepository documentRepository
    )
    {
        try
        {
            var metadata = await secEdgarClient.GetCompanyMetadata(company.Cik);

            // Persist the SEC classification first — unlike the fiscal year-end it
            // has no fallback chain, so it must not be skipped by the early returns
            // below. Blank values normalise to null and stay eligible for a refill.
            await commonStockManager.SetSecClassification(
                company,
                metadata?.Sic,
                metadata?.EntityType
            );

            if (metadata?.FiscalYearEndMonth is { } month)
            {
                await commonStockManager.SetFiscalYearEnd(
                    company,
                    month,
                    metadata.FiscalYearEndDay
                );
                return;
            }

            // SEC metadata lacks fiscal year-end — infer from most recent 10-K.
            // A 10-K's ReportingForDate is the period end, which is the fiscal
            // year-end by definition.
            var latestTenK = await documentRepository
                .GetByCompany(company)
                .Where(d => d.DocumentType == DocumentType.TenK)
                .OrderByDescending(d => d.ReportingForDate)
                .Select(d => new { d.ReportingForDate })
                .FirstOrDefaultAsync();

            if (latestTenK is not null)
            {
                _logger.LogInformation(
                    "SEC metadata has no fiscal year-end for {Ticker}; inferred from 10-K period ending {Date}",
                    company.Ticker,
                    latestTenK.ReportingForDate
                );
                await commonStockManager.SetFiscalYearEnd(
                    company,
                    latestTenK.ReportingForDate.Month,
                    latestTenK.ReportingForDate.Day
                );
                return;
            }

            // No 10-K filings — try annual foreign-filer forms from the cached
            // submissions JSON (zero extra SEC requests), 20-F first then 40-F.
            foreach (var (filter, formName) in ForeignFilerAnnualForms)
            {
                var reportDate = await secEdgarClient.GetMostRecentReportDate(company.Cik, filter);
                if (reportDate is { } date)
                {
                    _logger.LogInformation(
                        "SEC metadata has no fiscal year-end for {Ticker}; inferred from {Form} report date {Date}",
                        company.Ticker,
                        formName,
                        date
                    );
                    await commonStockManager.SetFiscalYearEnd(company, date.Month, date.Day);
                    return;
                }
            }

            if (company.FiscalYearEndMonth is null)
            {
                _logger.LogWarning(
                    "No fiscal year-end source for {Ticker} (CIK: {Cik}): SEC metadata returned null, no 10-K filings exist, and no 20-F/40-F found in recent submissions",
                    company.Ticker,
                    company.Cik
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not update SEC metadata for {Ticker} (CIK: {Cik}): {Message}",
                company.Ticker,
                company.Cik,
                ex.Message
            );
            await ReportError(
                "UpdateCompanyMetadata",
                ex,
                $"ticker: {company.Ticker}, cik: {company.Cik}"
            );
        }
    }

    private async Task ProcessDocumentTypeForCompany(
        CommonStock company,
        DocumentType documentType,
        DocumentTypeFilter secFilter,
        ScrapingResult result,
        ISecEdgarClient secEdgarClient,
        IDocumentPersistenceService persistenceService
    )
    {
        _logger.LogDebug(
            "Fetching {DocumentType} filings for {Ticker}",
            documentType,
            company.Ticker
        );

        try
        {
            var filings = await CollectFilingsAcrossCiks(
                company,
                documentType,
                secFilter,
                result,
                secEdgarClient
            );

            result.DocumentsFound += filings.Count;

            // The filing list re-enumerates history from MinSyncDate every cycle,
            // so dedup it in one batched lookup instead of one DB round-trip (and,
            // for processor forms, one DI scope) per already-ingested filing. The
            // per-filing checks inside ProcessFiling stay as the race guard for
            // the few filings that survive the prefilter.
            var newFilings = await FilterAlreadyIngested(
                company,
                documentType,
                filings,
                persistenceService
            );

            // Drop filings tombstoned by a previous deterministic ingest failure
            // whose retry backoff hasn't elapsed — otherwise each enumeration
            // re-downloads the multi-MB submission just to fail identically.
            newFilings = await FilterTombstonedFilings(newFilings);

            result.DocumentsSkipped += filings.Count - newFilings.Count;

            foreach (var filing in newFilings)
            {
                await ProcessFiling(company, filing, documentType, result, persistenceService);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing {DocumentType} documents for company {Ticker}",
                documentType,
                company.Ticker
            );
            RecordError(result, $"Company {company.Ticker} - {documentType}", ex);
            await ReportError(
                "ProcessDocType",
                ex,
                $"ticker: {company.Ticker}, type: {documentType}"
            );
        }
    }

    // Drops the filings this pass has already ingested, using one batched lookup per
    // (company, type): the processor's known-accession set for processor forms, or the
    // document store's known accessions + legacy pre-accession keys for plain forms.
    private async Task<List<FilingData>> FilterAlreadyIngested(
        CommonStock company,
        DocumentType documentType,
        List<FilingData> filings,
        IDocumentPersistenceService persistenceService
    )
    {
        if (filings.Count == 0)
            return filings;

        var accessions = filings
            .Select(f => f.AccessionNumber)
            .Where(a => !string.IsNullOrEmpty(a))
            .Distinct()
            .ToList();

        var processor = _filingProcessors.FirstOrDefault(p => p.CanProcess(documentType));
        if (processor != null)
        {
            var known = await processor.FilterKnownAccessions(accessions) ?? [];
            return filings
                .Where(f =>
                    string.IsNullOrEmpty(f.AccessionNumber) || !known.Contains(f.AccessionNumber)
                )
                .ToList();
        }

        var (knownAccessions, legacyKeys) = await persistenceService.GetKnownFilingKeys(
            company,
            documentType,
            accessions
        );
        knownAccessions ??= [];
        legacyKeys ??= [];

        return filings
            .Where(f =>
                !(
                    (
                        !string.IsNullOrEmpty(f.AccessionNumber)
                        && knownAccessions.Contains(f.AccessionNumber)
                    ) || legacyKeys.Contains((f.FilingDate, f.ReportDate))
                )
            )
            .ToList();
    }

    // Subsidiary CIKs share the parent's public ticker (e.g. parent + co-registrant
    // operating sub). Their filings belong on the parent's stock page, so we fetch
    // each CIK separately and dedupe by AccessionNumber (globally unique in SEC).
    private async Task<List<FilingData>> CollectFilingsAcrossCiks(
        CommonStock company,
        DocumentType documentType,
        DocumentTypeFilter secFilter,
        ScrapingResult result,
        ISecEdgarClient secEdgarClient
    )
    {
        var ciks = new List<string> { company.Cik };
        ciks.AddRange(company.SecondaryCiks);

        var fromDate =
            _workerOptions.MinSyncDate != null
                ? DateOnly.FromDateTime(_workerOptions.MinSyncDate.Value)
                : (DateOnly?)null;

        var filings = new List<FilingData>();
        var seenAccessions = new HashSet<string>();

        foreach (var cik in ciks)
        {
            List<FilingData> cikFilings;
            try
            {
                cikFilings = await secEdgarClient.GetCompanyFilings(cik, secFilter, fromDate);
            }
            catch (HttpRequestException ex)
            {
                // One CIK failing shouldn't drop the others — log and continue.
                _logger.LogWarning(
                    ex,
                    "HTTP error fetching {DocumentType} filings for CIK {Cik} ({Ticker})",
                    documentType,
                    cik,
                    company.Ticker
                );
                RecordError(result, $"Company {company.Ticker} CIK {cik} - {documentType}", ex);
                continue;
            }

            foreach (var filing in cikFilings)
            {
                if (seenAccessions.Add(filing.AccessionNumber))
                    filings.Add(filing);
            }
        }

        _logger.LogDebug(
            "Found {FilingCount} {DocumentType} filings for {Ticker} across {CikCount} CIK(s)",
            filings.Count,
            documentType,
            company.Ticker,
            ciks.Count
        );

        // Oldest first (accession breaks same-day ties — SEC assigns them
        // monotonically per filer agent): EDGAR lists newest-first, and pipelines
        // with supersession semantics (a Form 4/A replacing its original's
        // transactions) want the original ingested before its amendment whenever
        // both are in one pass — the out-of-order guards then only cover
        // cross-cycle arrivals.
        return filings
            .OrderBy(f => f.FilingDate)
            .ThenBy(f => f.AccessionNumber, StringComparer.Ordinal)
            .ToList();
    }

    private async Task ProcessFiling(
        CommonStock company,
        FilingData filing,
        DocumentType documentType,
        ScrapingResult result,
        IDocumentPersistenceService persistenceService
    )
    {
        try
        {
            var detectedType = DocumentTypeExtensions.FromFormName(filing.Form);
            if (detectedType == null)
            {
                _logger.LogWarning(
                    "Unknown form type '{Form}' for {Ticker} - skipping",
                    filing.Form,
                    company.Ticker
                );
                result.DocumentsSkipped++;
                return;
            }

            documentType = detectedType;

            var processor = _filingProcessors.FirstOrDefault(p => p.CanProcess(documentType));
            if (processor != null)
            {
                var processed = await processor.Process(filing, company);
                if (processed)
                    result.DocumentsAdded++;
                else
                    result.DocumentsSkipped++;
                return;
            }

            if (
                await persistenceService.Exists(
                    company,
                    documentType,
                    filing.FilingDate,
                    filing.ReportDate,
                    filing.AccessionNumber
                )
            )
            {
                result.DocumentsSkipped++;
                return;
            }

            if (!await CreateDocument(company, filing, documentType))
            {
                result.DocumentsSkipped++;
                return;
            }

            result.DocumentsAdded++;
            await ClearFilingIngestTombstone(filing.AccessionNumber);

            _logger.LogInformation(
                "Added document for {Ticker} - {DocumentType} - {FilingDate}",
                company.Ticker,
                documentType,
                filing.FilingDate
            );
        }
        catch (InvalidOperationException ex)
        {
            // Deterministic normalization failures — defer to retry after all tickers
            _logger.LogWarning(
                ex,
                "Deferring document for {Ticker} - {DocumentType} - {FilingDate}: {Message}",
                company.Ticker,
                documentType,
                filing.FilingDate,
                ex.Message
            );
            result.DeferredFilings.Add(new DeferredFiling(company, filing, documentType));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "HTTP error processing filing for {Ticker} - {AccessionNumber}",
                company.Ticker,
                filing.AccessionNumber
            );
            RecordError(result, $"Filing {company.Ticker}/{filing.AccessionNumber}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing filing for {Ticker} - {AccessionNumber}",
                company.Ticker,
                filing.AccessionNumber
            );
            RecordError(result, $"Filing {company.Ticker}/{filing.AccessionNumber}", ex);
            await ReportError(
                "ProcessFiling",
                ex,
                $"ticker: {company.Ticker}, accession: {filing.AccessionNumber}"
            );
        }
    }

    private async Task RetryDeferredFilings(
        ScrapingResult result,
        CancellationToken cancellationToken
    )
    {
        _logger.LogInformation(
            "Retrying {Count} deferred filings after all tickers processed",
            result.DeferredFilings.Count
        );

        var deferred = result.DeferredFilings.ToList();
        result.DeferredFilings.Clear();

        foreach (var filing in deferred)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                if (!await CreateDocument(filing.Company, filing.Filing, filing.DocumentType))
                {
                    result.DocumentsSkipped++;
                    continue;
                }

                result.DocumentsAdded++;
                await ClearFilingIngestTombstone(filing.Filing.AccessionNumber);

                _logger.LogInformation(
                    "Deferred document succeeded for {Ticker} - {DocumentType} - {FilingDate}",
                    filing.Company.Ticker,
                    filing.DocumentType,
                    filing.Filing.FilingDate
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Skipping document for {Ticker} - {DocumentType} - {FilingDate} after retry: {Message}",
                    filing.Company.Ticker,
                    filing.DocumentType,
                    filing.Filing.FilingDate,
                    ex.Message
                );
                result.DocumentsSkipped++;

                // Tombstone ONLY the deterministic poison shape — the same
                // InvalidOperationException that deferred the filing. Anything
                // else (HTTP faults, timeouts as TaskCanceledException, deploy
                // -window DB errors) is transient: an infra blip must not put a
                // whole batch of ingestable filings on a multi-day backoff.
                if (ex is InvalidOperationException)
                    await RecordFilingIngestFailure(filing.Company, filing.Filing, ex);
            }
        }
    }

    // Drops filings whose failure tombstone says the retry backoff hasn't
    // elapsed. One batched lookup per (company, type); best-effort — a DB
    // hiccup falls back to processing everything, which is the old behavior.
    private async Task<List<FilingData>> FilterTombstonedFilings(List<FilingData> filings)
    {
        if (filings.Count == 0)
            return filings;

        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var tombstoneRepository =
                scope.ServiceProvider.GetRequiredService<FailedFilingIngestRepository>();

            var accessions = filings
                .Select(f => f.AccessionNumber)
                .Where(a => !string.IsNullOrEmpty(a))
                .ToList();

            var now = DateTime.UtcNow;
            var notDue = await tombstoneRepository
                .GetByAccessionNumbers(accessions)
                .Where(t => t.NextRetryAt > now)
                .Select(t => t.AccessionNumber)
                .ToListAsync();

            if (notDue.Count == 0)
                return filings;

            var notDueSet = notDue.ToHashSet();
            return filings.Where(f => !notDueSet.Contains(f.AccessionNumber)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tombstone prefilter failed; processing all filings");
            return filings;
        }
    }

    /// <summary>
    /// Records a deterministic ingest failure so future enumerations skip the
    /// filing until its backoff elapses. Best-effort: a failed write only means
    /// the filing is re-attempted next cycle, exactly as before tombstones.
    /// </summary>
    private async Task RecordFilingIngestFailure(
        CommonStock company,
        FilingData filing,
        Exception failure
    )
    {
        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            await FilingIngestTombstones.Record(
                scope.ServiceProvider.GetRequiredService<FailedFilingIngestRepository>(),
                company.Cik,
                filing,
                failure.Message,
                _logger
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not tombstone failed filing {AccessionNumber}",
                filing.AccessionNumber
            );
        }
    }

    // Removes a filing's failure tombstone once it finally ingests, keeping the
    // table meaningful as "currently failing". Best-effort; usually a PK miss.
    private async Task ClearFilingIngestTombstone(string accessionNumber)
    {
        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            await FilingIngestTombstones.Clear(
                scope.ServiceProvider.GetRequiredService<FailedFilingIngestRepository>(),
                accessionNumber,
                _logger
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not clear filing tombstone {AccessionNumber}",
                accessionNumber
            );
        }
    }

    // Returns true when the document is persisted (or already was); false on
    // the silent skip paths (no extractable content), so callers neither count
    // an add nor clear a failure tombstone for a filing that stored nothing.
    private async Task<bool> CreateDocument(
        CommonStock companyOutContext,
        FilingData filing,
        DocumentType documentType
    )
    {
        return await _retryPipeline.ExecuteAsync(
            async (cancellationToken) =>
            {
                await using var scope = _serviceScopeFactory.CreateAsyncScope();
                var secEdgarClient = scope.ServiceProvider.GetRequiredService<ISecEdgarClient>();
                var normalizer =
                    scope.ServiceProvider.GetRequiredService<ISecDocumentHtmlNormalizer>();
                var converter =
                    scope.ServiceProvider.GetRequiredService<ISecDocumentHtmlToMarkdownConverter>();
                var pdfTextExtractor =
                    scope.ServiceProvider.GetRequiredService<IPdfTextExtractor>();
                var persistenceService =
                    scope.ServiceProvider.GetRequiredService<IDocumentPersistenceService>();
                var companyRepository =
                    scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

                var company = await companyRepository.Get(companyOutContext.Id);

                // The caller's dedup check runs OUTSIDE this retry pipeline, and
                // Save commits its transaction before the post-commit publish.
                // A transient failure after the commit (bus hiccup, commit-ack
                // timeout where the commit actually landed) re-runs this whole
                // callback, and with no unique index on AccessionNumber each
                // retry inserted a duplicate Document row. Re-check inside the
                // retried callback so a retry after a committed save is a no-op.
                if (
                    await persistenceService.Exists(
                        company,
                        documentType,
                        filing.FilingDate,
                        filing.ReportDate,
                        filing.AccessionNumber
                    )
                )
                    return true;

                var content = await secEdgarClient.GetDocumentContent(filing);

                // Resolve the raw XBRL envelope from the submission we already fetched
                // (opt-in, best-effort, no extra EDGAR round-trip). The result is stored on
                // the document by the persistence service below.
                var xbrlCapture =
                    scope.ServiceProvider.GetRequiredService<XbrlEnvelopeCaptureService>();
                var xbrl = xbrlCapture.Capture(content, filing);

                // Stitch the as-filed HTML (cover page + exhibits) for the forms that carry
                // linked exhibits — built from the same submission, so no extra EDGAR fetch.
                // Best-effort: a malformed envelope must not break ingest, so a failure leaves
                // AsFiledHtmlVersion at 0 for the backfill to retry.
                AsFiledHtmlCaptureResult asFiledHtml = null;
                if (AsFiledHtmlCaptureService.AppliesTo(documentType))
                {
                    var asFiledCapture =
                        scope.ServiceProvider.GetRequiredService<AsFiledHtmlCaptureService>();
                    try
                    {
                        asFiledHtml = await asFiledCapture.Capture(
                            content,
                            filing,
                            cancellationToken
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "As-filed HTML build failed for {Ticker} - {DocumentType} - {FilingDate}; backfill will retry.",
                            companyOutContext.Ticker,
                            documentType,
                            filing.FilingDate
                        );
                    }
                }

                var normalizedHtml = normalizer.Normalize(content);
                var markdownDocument = converter.Convert(normalizedHtml);

                if (string.IsNullOrWhiteSpace(markdownDocument))
                {
                    markdownDocument = await TryExtractPdfFallback(
                        secEdgarClient,
                        pdfTextExtractor,
                        content,
                        companyOutContext,
                        filing,
                        documentType,
                        cancellationToken
                    );
                    if (markdownDocument == null)
                        return false;
                }

                await persistenceService.Save(
                    company,
                    Encoding.UTF8.GetBytes(markdownDocument),
                    $"{company.Ticker}_{documentType.DisplayName}_{filing.FilingDate:yyyy-MM-dd}.txt",
                    documentType,
                    filing.FilingDate,
                    filing.ReportDate,
                    filing.DocumentUrl,
                    filing.AccessionNumber,
                    filing.Items,
                    xbrl,
                    asFiledHtml,
                    cancellationToken
                );

                _logger.LogInformation(
                    "Created document entity for {Ticker} - {DocumentType} - {FilingDate}",
                    companyOutContext.Ticker,
                    documentType,
                    filing.FilingDate
                );
                return true;
            }
        );
    }

    // Paper-filed submissions (typically older 6-K/20-F) wrap a uuencoded PDF
    // and have no HTML for the normalizer to consume. Fetch the standalone PDF
    // artifact and extract its text instead. Returns null to signal "skip this
    // filing" — the caller short-circuits without persisting.
    private async Task<string> TryExtractPdfFallback(
        ISecEdgarClient secEdgarClient,
        IPdfTextExtractor pdfTextExtractor,
        string content,
        CommonStock companyOutContext,
        FilingData filing,
        DocumentType documentType,
        CancellationToken cancellationToken
    )
    {
        if (!SecDocumentEnvelopeParser.TryExtractPaperPdfFilename(content, out var pdfFilename))
        {
            _logger.LogWarning(
                "Skipping document for {Ticker} - {DocumentType} - {FilingDate}: no content after conversion. URL: {Url}",
                companyOutContext.Ticker,
                documentType,
                filing.FilingDate,
                filing.DocumentUrl
            );
            return null;
        }

        var pdfBytes = await secEdgarClient.GetDocumentFileBytes(
            filing.Cik,
            filing.AccessionNumber,
            pdfFilename,
            cancellationToken
        );
        var markdown = pdfTextExtractor.Extract(pdfBytes);

        if (string.IsNullOrWhiteSpace(markdown))
        {
            // PDF was located but yielded no extractable text — either the artifact was
            // missing (404, logged by the client), or it's an image-only scan needing OCR.
            _logger.LogWarning(
                "Skipping document for {Ticker} - {DocumentType} - {FilingDate}: PDF {Filename} produced no text. URL: {Url}",
                companyOutContext.Ticker,
                documentType,
                filing.FilingDate,
                pdfFilename,
                filing.DocumentUrl
            );
            return null;
        }

        _logger.LogInformation(
            "Extracted PDF text for {Ticker} - {DocumentType} - {FilingDate} from paper filing artifact {Filename}",
            companyOutContext.Ticker,
            documentType,
            filing.FilingDate,
            pdfFilename
        );

        return markdown;
    }

    private static void RecordError(ScrapingResult result, string label, Exception ex)
    {
        result.Errors++;
        result.ErrorMessages.Add($"{label}: {ex.Message}");
    }

    private Task ReportError(string operation, Exception ex, string requestSummary = null) =>
        _errorReporter.Report(
            ErrorSource.DocumentScraper,
            $"DocumentScraper.{operation}",
            ex,
            requestSummary
        );

    private ResiliencePipeline BuildRetryPipeline()
    {
        const int maxRetryAttempts = 3;

        return new ResiliencePipelineBuilder()
            .AddRetry(
                new RetryStrategyOptions
                {
                    ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    MaxRetryAttempts = maxRetryAttempts,
                    Delay = TimeSpan.FromSeconds(2),
                    OnRetry = context =>
                    {
                        if (context.AttemptNumber < maxRetryAttempts - 1)
                        {
                            _logger.LogWarning(
                                context.Outcome.Exception,
                                "Retrying document creation. Attempt {AttemptNumber}/{MaxRetries}",
                                context.AttemptNumber,
                                maxRetryAttempts
                            );
                        }
                        else
                        {
                            _logger.LogError(
                                context.Outcome.Exception,
                                "Document creation failed after {AttemptNumber} attempts",
                                context.AttemptNumber
                            );
                        }

                        return ValueTask.CompletedTask;
                    },
                }
            )
            .Build();
    }
}
