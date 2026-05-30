using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService;

public class FormAdvScraperWorker : BaseScraperWorker
{
    private readonly IConfiguration _configuration;

    protected override string WorkerName => "Form ADV scraper";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.FormAdvScraper;

    // Staggered behind the other SEC scrapers so they don't all drain the shared
    // EDGAR request budget at deploy time before the time-sensitive sweeps run.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(7);

    public FormAdvScraperWorker(
        ILogger<FormAdvScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<FormAdvScraperOptions> options,
        IConfiguration configuration
    )
        : base(logger, scopeFactory, errorReporter)
    {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration()
    {
        if (string.IsNullOrEmpty(_configuration["Sec:ContactEmail"]))
        {
            Logger.LogWarning(
                "Form ADV Scraper stopped: SEC_CONTACT_EMAIL not configured. Set it in your .env file."
            );
            return false;
        }
        return true;
    }

    protected override Task DoWork(CancellationToken stoppingToken) =>
        RunImport<FormAdvImportService>(stoppingToken);
}
