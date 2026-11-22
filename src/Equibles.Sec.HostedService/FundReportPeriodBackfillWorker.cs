using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Dates stored fund reports whose period EDGAR's index left blank, from their own cover. Ingestion dates new ones the
/// same way, so this drains a finite cohort; it reads stored text only (no EDGAR request), and a daily pass keeps the
/// few covers that state no period from being read more often.
/// </summary>
public class FundReportPeriodBackfillWorker : BaseScraperWorker
{
    protected override string WorkerName => "Fund report period backfill";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(24);
    protected override ErrorSource ErrorSource => ErrorSource.DocumentScraper;
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(10);

    public FundReportPeriodBackfillWorker(
        ILogger<FundReportPeriodBackfillWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter
    )
        : base(logger, scopeFactory, errorReporter) { }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<FundReportPeriodBackfillService>()
            .Backfill(stoppingToken);
        Logger.LogInformation(
            "Fund report period backfill complete. Undated: {Selected}, dated from their cover: {Dated}, unreadable: {Unreadable}",
            result.Selected,
            result.Dated,
            result.Unreadable
        );
    }
}
