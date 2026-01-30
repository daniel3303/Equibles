using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Finra.Contracts;
using Equibles.ShortData.HostedService.Configuration;
using Equibles.ShortData.HostedService.Services;
using Microsoft.Extensions.Options;

namespace Equibles.ShortData.HostedService;

public class ShortDataScraperWorker : BackgroundService {
    private readonly ILogger<ShortDataScraperWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _sleepInterval;

    public ShortDataScraperWorker(
        ILogger<ShortDataScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<FinraScraperOptions> options
    ) {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _sleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            _logger.LogInformation("Short data scraper worker running at: {Time}", DateTimeOffset.Now);
            await DoWork(stoppingToken);

            GarbageCollectorUtil.ForceAggressiveCollection();

            _logger.LogInformation("Short data import cycle complete. Sleeping for {Hours}h", _sleepInterval.TotalHours);
            await Task.Delay(_sleepInterval, stoppingToken);
        }
    }

    private async Task DoWork(CancellationToken cancellationToken) {
        try {
            // Phase 1: Import fails-to-deliver (also seeds CUSIPs on CommonStocks)
            _logger.LogInformation("Starting fails-to-deliver import");
            using (var scope = _scopeFactory.CreateScope()) {
                var ftdService = scope.ServiceProvider.GetRequiredService<FtdImportService>();
                await ftdService.Import(cancellationToken);
            }

            GarbageCollectorUtil.ForceAggressiveCollection();

            // Phases 2 & 3 require FINRA API credentials
            using (var checkScope = _scopeFactory.CreateScope()) {
                var finraClient = checkScope.ServiceProvider.GetRequiredService<IFinraClient>();
                if (!finraClient.IsConfigured) {
                    _logger.LogInformation("FINRA API credentials not configured, skipping short volume and short interest import");
                    return;
                }
            }

            // Phase 2: Import daily short volume
            _logger.LogInformation("Starting daily short volume import");
            using (var scope = _scopeFactory.CreateScope()) {
                var shortVolumeService = scope.ServiceProvider.GetRequiredService<ShortVolumeImportService>();
                await shortVolumeService.Import(cancellationToken);
            }

            GarbageCollectorUtil.ForceAggressiveCollection();

            // Phase 3: Import short interest
            _logger.LogInformation("Starting short interest import");
            using (var scope = _scopeFactory.CreateScope()) {
                var shortInterestService = scope.ServiceProvider.GetRequiredService<ShortInterestImportService>();
                await shortInterestService.Import(cancellationToken);
            }
        } catch (OperationCanceledException) {
            _logger.LogInformation("Short data scraper worker cancelled");
        } catch (Exception ex) {
            _logger.LogCritical(ex, "Critical error in short data scraper worker");
            await ReportError("ShortDataScraperWorker.DoWork", ex.Message, ex.StackTrace);
        }
    }

    private async Task ReportError(string context, string message, string stackTrace, string requestSummary = null) {
        try {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var errorManager = scope.ServiceProvider.GetRequiredService<ErrorManager>();
            await errorManager.Create(ErrorSource.ShortDataScraper, context, message, stackTrace, requestSummary);
        } catch { }
    }
}
