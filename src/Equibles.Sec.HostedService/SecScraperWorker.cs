using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Worker;
using Microsoft.Extensions.Configuration;

namespace Equibles.Sec.HostedService;

public class SecScraperWorker : BaseScraperWorker {
    private readonly IConfiguration _configuration;

    protected override string WorkerName => "SEC filing scraper";
    protected override TimeSpan SleepInterval => TimeSpan.FromSeconds(15);
    protected override ErrorSource ErrorSource => ErrorSource.DocumentScraper;

    public SecScraperWorker(
        ILogger<SecScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IConfiguration configuration
    ) : base(logger, scopeFactory, errorReporter) {
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration() {
        if (string.IsNullOrEmpty(_configuration["Sec:ContactEmail"])) {
            Logger.LogWarning("SEC Filing Scraper stopped: SEC_CONTACT_EMAIL not configured. Set it in your .env file.");
            return false;
        }
        return true;
    }

    protected override async Task DoWork(CancellationToken stoppingToken) {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var documentScraper = scope.ServiceProvider.GetRequiredService<IDocumentScraper>();
        var result = await documentScraper.ScrapeDocuments(stoppingToken);

        Logger.LogInformation(
            "Document scraping completed. Companies: {Companies}, Documents added: {Added}, Errors: {Errors}, Duration: {Duration}",
            result.CompaniesProcessed, result.DocumentsAdded, result.Errors, result.Duration);

        if (result.Errors > 0) {
            Logger.LogWarning("Scraping completed with {ErrorCount} errors", result.Errors);
        }
    }
}
