using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Fred.HostedService.Configuration;
using Equibles.Fred.HostedService.Services;
using Equibles.Integrations.Fred.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Fred.HostedService;

public class FredScraperWorker : BackgroundService {
    private readonly ILogger<FredScraperWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _sleepInterval;

    public FredScraperWorker(
        ILogger<FredScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<FredScraperOptions> options
    ) {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _sleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        // Verify FRED API key is configured
        using (var checkScope = _scopeFactory.CreateScope()) {
            var fredClient = checkScope.ServiceProvider.GetRequiredService<IFredClient>();
            if (!fredClient.IsConfigured) {
                _logger.LogWarning("FRED Scraper stopped: FRED__ApiKey not configured. Set it in your .env file.");
                return;
            }
        }

        while (!stoppingToken.IsCancellationRequested) {
            _logger.LogInformation("FRED scraper worker running at: {Time}", DateTimeOffset.Now);
            await DoWork(stoppingToken);

            GarbageCollectorUtil.ForceAggressiveCollection();

            _logger.LogInformation("FRED import cycle complete. Sleeping for {Hours}h", _sleepInterval.TotalHours);
            await Task.Delay(_sleepInterval, stoppingToken);
        }
    }

    private async Task DoWork(CancellationToken cancellationToken) {
        try {
            using var scope = _scopeFactory.CreateScope();
            var importService = scope.ServiceProvider.GetRequiredService<FredImportService>();
            await importService.Import(cancellationToken);
        } catch (OperationCanceledException) {
            _logger.LogInformation("FRED scraper worker cancelled");
        } catch (Exception ex) {
            _logger.LogCritical(ex, "Critical error in FRED scraper worker");
            await ReportError("FredScraperWorker.DoWork", ex.Message, ex.StackTrace);
        }
    }

    private async Task ReportError(string context, string message, string stackTrace, string requestSummary = null) {
        try {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var errorManager = scope.ServiceProvider.GetRequiredService<ErrorManager>();
            await errorManager.Create(ErrorSource.FredScraper, context, message, stackTrace, requestSummary);
        } catch { }
    }
}
