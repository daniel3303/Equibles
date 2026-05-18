using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService;

public class FtdScraperWorker : BaseScraperWorker
{
    private readonly IConfiguration _configuration;
    private readonly WorkerOptions _workerOptions;

    protected override string WorkerName => "FTD scraper";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.FtdScraper;

    public FtdScraperWorker(
        ILogger<FtdScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<FtdScraperOptions> options,
        IOptions<WorkerOptions> workerOptions,
        IConfiguration configuration
    )
        : base(logger, scopeFactory, errorReporter)
    {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
        _workerOptions = workerOptions.Value;
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration()
    {
        if (string.IsNullOrEmpty(_configuration["Sec:ContactEmail"]))
        {
            Logger.LogWarning(
                "FTD Scraper stopped: SEC_CONTACT_EMAIL not configured. Set it in your .env file."
            );
            return false;
        }
        return true;
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();

        // Cold-start guard (GH-851): the SEC CompanySync may not have populated
        // CommonStock yet. If the tracked universe is empty the FTD import would
        // match nothing and seed no CUSIPs, starving the Holdings pipeline for a
        // whole 24h cycle. Skip and retry soon instead.
        var tickerMapService = scope.ServiceProvider.GetRequiredService<TickerMapService>();
        var tickerMap = await tickerMapService.Build(_workerOptions.TickersToSync, stoppingToken);
        if (tickerMap.Count == 0)
        {
            Logger.LogInformation(
                "FTD scraper: tracked stock universe is empty (company sync pending) — skipping; will retry soon"
            );
            RequestRetrySoon();
            return;
        }

        var ftdService = scope.ServiceProvider.GetRequiredService<FtdImportService>();
        await ftdService.Import(stoppingToken);
    }
}
