using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService;

public class FtdScraperWorker : BaseScraperWorker {
    private readonly IConfiguration _configuration;

    protected override string WorkerName => "FTD scraper";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.FtdScraper;

    public FtdScraperWorker(
        ILogger<FtdScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<FtdScraperOptions> options,
        IConfiguration configuration
    ) : base(logger, scopeFactory, errorReporter) {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration() {
        if (string.IsNullOrEmpty(_configuration["Sec:ContactEmail"])) {
            Logger.LogWarning("FTD Scraper stopped: SEC_CONTACT_EMAIL not configured. Set it in your .env file.");
            return false;
        }
        return true;
    }

    protected override async Task DoWork(CancellationToken stoppingToken) {
        using var scope = ScopeFactory.CreateScope();
        var ftdService = scope.ServiceProvider.GetRequiredService<FtdImportService>();
        await ftdService.Import(stoppingToken);
    }
}
