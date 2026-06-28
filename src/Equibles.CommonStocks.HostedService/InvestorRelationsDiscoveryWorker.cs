using Equibles.CommonStocks.HostedService.Configuration;
using Equibles.CommonStocks.HostedService.Services;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.CommonStocks.HostedService;

/// <summary>
/// Periodically discovers each stock's investor-relations page URL, filling in
/// <c>CommonStock.InvestorRelationsUrl</c> incrementally.
/// </summary>
public class InvestorRelationsDiscoveryWorker : BaseScraperWorker
{
    protected override string WorkerName => "Investor relations discovery";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.InvestorRelationsDiscovery;

    public InvestorRelationsDiscoveryWorker(
        ILogger<InvestorRelationsDiscoveryWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<InvestorRelationsDiscoveryOptions> options
    )
        : base(logger, scopeFactory, errorReporter)
    {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<InvestorRelationsDiscoveryService>();

        // Drain a large backlog in successive bursts: when a cycle fills a full batch there are
        // still pending stocks, so chain into the next batch after the short ContinuationInterval
        // instead of sleeping the full SleepInterval (one batch per cycle).
        if (await service.DiscoverBatch(stoppingToken))
        {
            RequestImmediateContinuation();
        }
    }
}
