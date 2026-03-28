using System.Text;
using Equibles.Errors.BusinessLogic;

using Equibles.Errors.Data.Models;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Sec.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Core.Configuration;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Extensions;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Equibles.Sec.HostedService;

public class DocumentScraper : IDocumentScraper {
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ICompanySyncService _companySyncService;
    private readonly IEnumerable<IFilingProcessor> _filingProcessors;
    private readonly DocumentScraperOptions _options;
    private readonly WorkerOptions _workerOptions;
    private readonly ILogger<DocumentScraper> _logger;
    private readonly ErrorReporter _errorReporter;
    private readonly ResiliencePipeline _retryPipeline;

    public DocumentScraper(IServiceScopeFactory serviceScopeFactory,
        ICompanySyncService companySyncService,
        IEnumerable<IFilingProcessor> filingProcessors,
        IOptions<DocumentScraperOptions> options,
        IOptions<WorkerOptions> workerOptions,
        ILogger<DocumentScraper> logger,
        ErrorReporter errorReporter
    ) {
        _serviceScopeFactory = serviceScopeFactory;
        _companySyncService = companySyncService;
        _filingProcessors = filingProcessors;
        _options = options.Value;
        _workerOptions = workerOptions.Value;
        _logger = logger;
        _errorReporter = errorReporter;
        _retryPipeline = BuildRetryPipeline();
    }

    public async Task<ScrapingResult> ScrapeDocuments(CancellationToken cancellationToken = default) {
        var result = new ScrapingResult();
        var startTime = DateTime.UtcNow;

        try {
            _logger.LogInformation("Starting document scraping process...");

            // Step 1: Sync companies from SEC API to database
            await _companySyncService.SyncCompaniesFromSecApi();
            GarbageCollectorUtil.ForceAggressiveCollection();

            // Step 2: Process SEC documents for each company
            var companiesUntracked = await GetAllCompaniesWithNoTracking();
            _logger.LogInformation("Found {CompanyCount} companies to process for documents", companiesUntracked.Count);

            foreach (var companyUntracked in companiesUntracked) {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await ProcessCompanyDocumentsWithScope(companyUntracked, result);
                GarbageCollectorUtil.ForceAggressiveCollection();
                result.CompaniesProcessed++;
            }

            // Step 3: Retry deferred filings (normalization failures) after all tickers
            if (result.DeferredFilings.Count > 0) {
                _logger.LogInformation("Retrying {Count} deferred filings after all tickers processed",
                    result.DeferredFilings.Count);

                var deferred = result.DeferredFilings.ToList();
                result.DeferredFilings.Clear();

                foreach (var filing in deferred) {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try {
                        await CreateDocument(filing.Company, filing.Filing, filing.DocumentType);
                        result.DocumentsAdded++;

                        _logger.LogInformation("Deferred document succeeded for {Ticker} - {DocumentType} - {FilingDate}",
                            filing.Company.Ticker, filing.DocumentType, filing.Filing.FilingDate);
                    } catch (Exception ex) {
                        _logger.LogWarning(ex,
                            "Skipping document for {Ticker} - {DocumentType} - {FilingDate} after retry: {Message}",
                            filing.Company.Ticker, filing.DocumentType, filing.Filing.FilingDate, ex.Message);
                        result.DocumentsSkipped++;
                    }

                    GarbageCollectorUtil.ForceAggressiveCollection();
                }
            }

            result.Duration = DateTime.UtcNow - startTime;

            _logger.LogInformation(
                "Document scraping completed. Processed: {CompaniesProcessed}, Found: {DocumentsFound}, Added: {DocumentsAdded}, Skipped: {DocumentsSkipped}, Errors: {Errors}",
                result.CompaniesProcessed, result.DocumentsFound, result.DocumentsAdded, result.DocumentsSkipped,
                result.Errors);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error during document scraping process");
            result.Errors++;
            result.ErrorMessages.Add($"General error: {ex.Message}");
            await _errorReporter.Report(ErrorSource.DocumentScraper, "DocumentScraper.ScrapeDocuments", ex.Message, ex.StackTrace);
        }

        return result;
    }

    private async Task<List<CommonStock>> GetAllCompaniesWithNoTracking() {
        using var scope = _serviceScopeFactory.CreateScope();
        var commonStockRepository = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        if (_workerOptions.TickersToSync?.Count > 0) {
            return await commonStockRepository.GetByTickers(_workerOptions.TickersToSync).ToListAsync();
        }

        return await commonStockRepository.GetAll().AsNoTracking().ToListAsync();
    }

    private async Task ProcessCompanyDocumentsWithScope(CommonStock companyUntracked, ScrapingResult result) {
        var startTime = DateTime.UtcNow;
        using var scope = _serviceScopeFactory.CreateScope();
        var companyRepository = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var secEdgarClient = scope.ServiceProvider.GetRequiredService<ISecEdgarClient>();
        var persistenceService = scope.ServiceProvider.GetRequiredService<IDocumentPersistenceService>();

        var company = await companyRepository.Get(companyUntracked.Id);

        try {
            _logger.LogInformation("Processing documents for company: {Ticker} - {Name}", company.Ticker, company.Name);

            foreach (var documentType in _options.DocumentTypesToSync) {
                var secFilter = documentType.ToSecEdgarFilter();
                if (secFilter == null) {
                    _logger.LogWarning("No SEC Edgar filter mapping found for document type: {DocumentType}",
                        documentType);
                    continue;
                }

                await ProcessDocumentTypeForCompany(company, documentType, secFilter.Value, result, secEdgarClient, persistenceService);
            }

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Completed processing documents for {Ticker} in {Duration}. Found: {DocumentsFound}, Added: {DocumentsAdded}, Skipped: {DocumentsSkipped}, Errors: {Errors}",
                company.Ticker, duration, result.DocumentsFound, result.DocumentsAdded, result.DocumentsSkipped, result.Errors);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error processing documents for company {Ticker}", company.Ticker);
            result.Errors++;
            result.ErrorMessages.Add($"Company {company.Ticker}: {ex.Message}");
            await _errorReporter.Report(ErrorSource.DocumentScraper, "DocumentScraper.ProcessCompany", ex.Message, ex.StackTrace, $"ticker: {company.Ticker}");
        }
    }

    private async Task ProcessDocumentTypeForCompany(CommonStock company,
        DocumentType documentType,
        DocumentTypeFilter secFilter,
        ScrapingResult result,
        ISecEdgarClient secEdgarClient,
        IDocumentPersistenceService persistenceService
    ) {
        _logger.LogDebug("Fetching {DocumentType} filings for {Ticker}", documentType, company.Ticker);

        try {
            var filings = await secEdgarClient.GetCompanyFilings(
                company.Cik,
                secFilter,
                _workerOptions.MinSyncDate != null ? DateOnly.FromDateTime(_workerOptions.MinSyncDate.Value) : null);

            _logger.LogDebug("Found {FilingCount} {DocumentType} filings for {Ticker}",
                filings.Count, documentType, company.Ticker);

            result.DocumentsFound += filings.Count;

            foreach (var filing in filings) {
                await ProcessFiling(company, filing, documentType, result, persistenceService);
            }
        } catch (HttpRequestException ex) {
            _logger.LogWarning(ex, "HTTP error processing {DocumentType} documents for company {Ticker}",
                documentType, company.Ticker);
            result.Errors++;
            result.ErrorMessages.Add($"Company {company.Ticker} - {documentType}: {ex.Message}");
        } catch (Exception ex) {
            _logger.LogError(ex, "Error processing {DocumentType} documents for company {Ticker}",
                documentType, company.Ticker);
            result.Errors++;
            result.ErrorMessages.Add($"Company {company.Ticker} - {documentType}: {ex.Message}");
            await _errorReporter.Report(ErrorSource.DocumentScraper, "DocumentScraper.ProcessDocType", ex.Message, ex.StackTrace, $"ticker: {company.Ticker}, type: {documentType}");
        }
    }

    private async Task ProcessFiling(CommonStock company,
        FilingData filing,
        DocumentType documentType,
        ScrapingResult result,
        IDocumentPersistenceService persistenceService
    ) {
        try {
            // Detect actual document type from SEC filing's form field
            var detectedType = DocumentTypeExtensions.FromFormName(filing.Form);
            if (detectedType == null) {
                _logger.LogWarning("Unknown form type '{Form}' for {Ticker} - skipping", filing.Form, company.Ticker);
                result.DocumentsSkipped++;
                return;
            }

            documentType = detectedType;

            // Check if a specialized processor handles this document type
            var processor = _filingProcessors.FirstOrDefault(p => p.CanProcess(documentType));
            if (processor != null) {
                var processed = await processor.Process(filing, company);
                if (processed) result.DocumentsAdded++;
                else result.DocumentsSkipped++;
                return;
            }

            // Default flow: check if document already exists, then create as markdown
            if (await persistenceService.Exists(company, documentType, filing.FilingDate, filing.ReportDate)) {
                result.DocumentsSkipped++;
                return;
            }

            await CreateDocument(company, filing, documentType);
            result.DocumentsAdded++;

            _logger.LogInformation("Added document for {Ticker} - {DocumentType} - {FilingDate}",
                company.Ticker, documentType, filing.FilingDate);
        } catch (InvalidOperationException ex) {
            // Deterministic normalization failures — defer to retry after all tickers
            _logger.LogWarning(ex, "Deferring document for {Ticker} - {DocumentType} - {FilingDate}: {Message}",
                company.Ticker, documentType, filing.FilingDate, ex.Message);
            result.DeferredFilings.Add(new DeferredFiling(company, filing, documentType));
        } catch (HttpRequestException ex) {
            _logger.LogWarning(ex, "HTTP error processing filing for {Ticker} - {AccessionNumber}",
                company.Ticker, filing.AccessionNumber);
            result.Errors++;
            result.ErrorMessages.Add($"Filing {company.Ticker}/{filing.AccessionNumber}: {ex.Message}");
        } catch (Exception ex) {
            _logger.LogError(ex, "Error processing filing for {Ticker} - {AccessionNumber}",
                company.Ticker, filing.AccessionNumber);
            result.Errors++;
            result.ErrorMessages.Add($"Filing {company.Ticker}/{filing.AccessionNumber}: {ex.Message}");
            await _errorReporter.Report(ErrorSource.DocumentScraper, "DocumentScraper.ProcessFiling", ex.Message, ex.StackTrace, $"ticker: {company.Ticker}, accession: {filing.AccessionNumber}");
        }
    }

    private async Task CreateDocument(CommonStock companyOutContext,
        FilingData filing,
        DocumentType documentType
    ) {
        await _retryPipeline.ExecuteAsync(async (cancellationToken) => {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var secEdgarClient = scope.ServiceProvider.GetRequiredService<ISecEdgarClient>();
            var normalizer = scope.ServiceProvider.GetRequiredService<ISecDocumentHtmlNormalizer>();
            var converter = scope.ServiceProvider.GetRequiredService<ISecDocumentHtmlToMarkdownConverter>();
            var persistenceService = scope.ServiceProvider.GetRequiredService<IDocumentPersistenceService>();
            var companyRepository = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

            var company = await companyRepository.Get(companyOutContext.Id);
            var content = await secEdgarClient.GetDocumentContent(filing);

            var normalizedHtml = normalizer.Normalize(content);
            var markdownDocument = converter.Convert(normalizedHtml);

            if (string.IsNullOrWhiteSpace(markdownDocument)) {
                _logger.LogWarning("Skipping document for {Ticker} - {DocumentType} - {FilingDate}: no content after conversion. URL: {Url}",
                    companyOutContext.Ticker, documentType, filing.FilingDate, filing.DocumentUrl);
                return;
            }

            await persistenceService.Save(company, Encoding.UTF8.GetBytes(markdownDocument),
                $"{company.Ticker}_{documentType.DisplayName}_{filing.FilingDate:yyyy-MM-dd}.txt",
                documentType, filing.FilingDate, filing.ReportDate, filing.DocumentUrl, cancellationToken);

            _logger.LogInformation(
                "Created document entity for {Ticker} - {DocumentType} - {FilingDate}",
                companyOutContext.Ticker, documentType, filing.FilingDate);
        });
    }

    private ResiliencePipeline BuildRetryPipeline() {
        const int maxRetryAttempts = 3;

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                MaxRetryAttempts = maxRetryAttempts,
                Delay = TimeSpan.FromSeconds(2),
                OnRetry = context => {
                    if (context.AttemptNumber < maxRetryAttempts) {
                        _logger.LogWarning(context.Outcome.Exception,
                            "Retrying document creation. Attempt {AttemptNumber}/{MaxRetries}",
                            context.AttemptNumber, maxRetryAttempts);
                    } else {
                        _logger.LogError(context.Outcome.Exception,
                            "Document creation failed after {AttemptNumber} attempts",
                            context.AttemptNumber);
                    }

                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

}
