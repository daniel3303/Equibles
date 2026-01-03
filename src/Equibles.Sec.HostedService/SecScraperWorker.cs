using Equibles.Errors.BusinessLogic;

using Equibles.Errors.Data.Models;

namespace Equibles.Sec.HostedService;

public class SecScraperWorker : BackgroundService {
    private readonly ILogger<SecScraperWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _intervalBetweenExecutions;

    public SecScraperWorker(ILogger<SecScraperWorker> logger, IServiceScopeFactory scopeFactory) {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _intervalBetweenExecutions = TimeSpan.FromSeconds(15);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            _logger.LogInformation("Document scraper worker running at: {Time}", DateTimeOffset.Now);
            await DoWork(stoppingToken);

            // Force freeing memory
            GarbageCollectorUtil.ForceAggressiveCollection();

            // Thread sleep with cancellation token
            await Task.Delay(_intervalBetweenExecutions, stoppingToken);
        }
    }

    private async Task DoWork(CancellationToken cancellationToken) {
        try {
            using var scope = _scopeFactory.CreateScope();
            var documenScrapper = scope.ServiceProvider.GetRequiredService<IDocumentScraper>();

            var result = await documenScrapper.ScrapeDocuments(cancellationToken);

            _logger.LogInformation(
                "Document scraping completed. Companies: {Companies}, Documents added: {Added}, Errors: {Errors}, Duration: {Duration}",
                result.CompaniesProcessed, result.DocumentsAdded, result.Errors, result.Duration);

            if (result.Errors > 0) {
                _logger.LogWarning("Scraping completed with {ErrorCount} errors", result.Errors);
            }
        } catch (Exception e) {
            _logger.LogCritical(e, "Critical error while executing document scraper worker");
            await ReportError("SecScraperWorker.DoWork", e.Message, e.StackTrace);
        }
    }

    private async Task ReportError(string context, string message, string stackTrace, string requestSummary = null) {
        try {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var errorManager = scope.ServiceProvider.GetRequiredService<ErrorManager>();
            await errorManager.Create(ErrorSource.DocumentScraper, context, message, stackTrace, requestSummary);
        } catch { }
    }
}