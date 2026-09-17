using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService;

public class EsefReportScraperWorker : BaseScraperWorker
{
    protected override string WorkerName => "ESEF report scraper";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.EsefReportScraper;

    // Staggered like the other document scrapers so a deploy does not start every sweep at once.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(11);

    public EsefReportScraperWorker(
        ILogger<EsefReportScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<EsefReportScraperOptions> options
    )
        : base(logger, scopeFactory, errorReporter)
    {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
    }

    protected override Task DoWork(CancellationToken stoppingToken) =>
        RunImport<EsefReportImportService>(stoppingToken);
}
