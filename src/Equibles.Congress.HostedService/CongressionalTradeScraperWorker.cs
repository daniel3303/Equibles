using Equibles.Errors.BusinessLogic;

using Equibles.Errors.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.Congress.HostedService;

public class CongressionalTradeScraperWorker : BackgroundService {
    private readonly ILogger<CongressionalTradeScraperWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    public CongressionalTradeScraperWorker(
        ILogger<CongressionalTradeScraperWorker> logger,
        IServiceScopeFactory scopeFactory
    ) {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            _logger.LogInformation("Congressional trade scraper running at: {Time}", DateTimeOffset.Now);
            await DoWork(stoppingToken);

            GarbageCollectorUtil.ForceAggressiveCollection();
            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task DoWork(CancellationToken ct) {
        try {
            using var scope = _scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<CongressionalTradeSyncService>();
            await syncService.SyncAll(ct);

            _logger.LogInformation("Congressional trade sync completed");
        } catch (OperationCanceledException) {
            _logger.LogInformation("Congressional trade scraper was cancelled");
        } catch (Exception ex) {
            _logger.LogCritical(ex, "Critical error in congressional trade scraper");
            await ReportError("CongressionalTradeScraperWorker.DoWork", ex.Message, ex.StackTrace);
        }
    }

    private async Task ReportError(string context, string message, string stackTrace) {
        try {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var errorManager = scope.ServiceProvider.GetRequiredService<ErrorManager>();
            await errorManager.Create(ErrorSource.CongressScraper, context, message, stackTrace);
        } catch (Exception ex) {
            _logger.LogDebug(ex, "Failed to report error for {Context}", context);
        }
    }
}
