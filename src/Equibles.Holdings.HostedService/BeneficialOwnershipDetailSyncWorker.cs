using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Configuration;

namespace Equibles.Holdings.HostedService;

/// <summary>
/// Keeps every stored 13D/13G filing's cover-page details on file: it drains the filings that
/// have none on the shared SEC limiter, then re-checks every few hours.
/// </summary>
public class BeneficialOwnershipDetailSyncWorker : BaseScraperWorker
{
    // About two minutes of SEC requests per cycle; the worker continues at once while more remain.
    internal const int FilingsPerCycle = 500;

    private readonly IConfiguration _configuration;

    protected override string WorkerName => "13D/13G detail sync";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(6);
    protected override ErrorSource ErrorSource => ErrorSource.HoldingsScraper;

    // Staggered behind the realtime 13D/13G sweep, which writes new filings' details itself.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(17);

    public BeneficialOwnershipDetailSyncWorker(
        ILogger<BeneficialOwnershipDetailSyncWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IConfiguration configuration
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration() =>
        ValidateSecContactEmail(
            _configuration,
            "13D/13G detail sync",
            treatWhitespaceAsAbsent: true
        );

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var sync = scope.ServiceProvider.GetRequiredService<BeneficialOwnershipDetailSyncService>();
        if (await sync.SyncMissing(FilingsPerCycle, stoppingToken))
            RequestImmediateContinuation();
    }
}
