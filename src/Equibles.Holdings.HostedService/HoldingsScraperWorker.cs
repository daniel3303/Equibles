using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Holdings.HostedService.Configuration;
using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Equibles.Holdings.HostedService;

public class HoldingsScraperWorker : BackgroundService {
    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays = [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
    ];

    private readonly ILogger<HoldingsScraperWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HoldingsScraperOptions _options;
    private readonly TimeSpan _sleepInterval = TimeSpan.FromHours(24);

    private readonly IConfiguration _configuration;

    public HoldingsScraperWorker(
        ILogger<HoldingsScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<HoldingsScraperOptions> options,
        IConfiguration configuration
    ) {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (string.IsNullOrEmpty(_configuration["Sec:ContactEmail"])) {
            _logger.LogWarning("Holdings Scraper stopped: SEC_CONTACT_EMAIL not configured. Set it in your .env file.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested) {
            _logger.LogInformation("Holdings scraper worker running at: {Time}", DateTimeOffset.Now);
            await DoWork(stoppingToken);

            GarbageCollectorUtil.ForceAggressiveCollection();

            _logger.LogInformation("Holdings import cycle complete. Sleeping for {Hours}h", _sleepInterval.TotalHours);
            await Task.Delay(_sleepInterval, stoppingToken);
        }
    }

    private async Task DoWork(CancellationToken cancellationToken) {
        try {
            var startDate = _options.MinScrapingDate ?? new DateTime(2020, 1, 1);
            var minReportDate = DateOnly.FromDateTime(startDate);
            var fileNames = HoldingsDataSetClient.GetDataSetFileNames(startDate);

            _logger.LogInformation(
                "Processing {Count} quarterly data sets from {Start:yyyy-MM-dd}",
                fileNames.Count, startDate);

            var failedDataSets = new List<string>();

            foreach (var fileName in fileNames) {
                cancellationToken.ThrowIfCancellationRequested();

                if (!await TryProcessDataSet(fileName, minReportDate, cancellationToken)) {
                    failedDataSets.Add(fileName);
                    // Cool down before next file to avoid cascading rate limits
                    await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                }
            }

            if (failedDataSets.Count > 0) {
                _logger.LogWarning(
                    "Retrying {Count} failed data sets at end of cycle: {FileNames}",
                    failedDataSets.Count, string.Join(", ", failedDataSets));

                foreach (var fileName in failedDataSets) {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!await TryProcessDataSet(fileName, minReportDate, cancellationToken)) {
                        _logger.LogError(
                            "Data set {FileName} permanently failed after all retry attempts in this cycle",
                            fileName);
                        await ReportError("Holdings.ProcessDataSet", "Permanently failed after all retry attempts", null, $"file: {fileName}, permanently failed");
                        // Cool down before next retry to avoid cascading rate limits
                        await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                    }
                }
            }
        } catch (OperationCanceledException) {
            _logger.LogInformation("Holdings scraper worker cancelled");
        } catch (Exception ex) {
            _logger.LogCritical(ex, "Critical error in holdings scraper worker");
            await ReportError("HoldingsScraperWorker.DoWork", ex.Message, ex.StackTrace);
        }
    }

    private async Task<bool> TryProcessDataSet(
        string fileName,
        DateOnly minReportDate,
        CancellationToken cancellationToken
    ) {
        for (var attempt = 1; attempt <= MaxRetries; attempt++) {
            try {
                if (attempt > 1) {
                    var delay = RetryDelays[Math.Min(attempt - 2, RetryDelays.Length - 1)];
                    _logger.LogInformation(
                        "Retry attempt {Attempt}/{MaxRetries} for {FileName} after {Delay}s",
                        attempt, MaxRetries, fileName, delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken);
                }

                _logger.LogInformation("Processing data set: {FileName}", fileName);

                using var scope = _scopeFactory.CreateScope();
                var dataSetClient = scope.ServiceProvider.GetRequiredService<HoldingsDataSetClient>();
                var importService = scope.ServiceProvider.GetRequiredService<HoldingsImportService>();

                var valueInThousands = HoldingsDataSetClient.IsValueInThousands(fileName);
                using var archive = await dataSetClient.DownloadDataSet(fileName, cancellationToken);
                await importService.ImportDataSet(archive, minReportDate, valueInThousands, cancellationToken);

                GarbageCollectorUtil.ForceAggressiveCollection();

                // SEC rate limiting: wait between downloads
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                return true;
            } catch (HttpRequestException ex) {
                _logger.LogError(
                    ex,
                    "Failed to download data set {FileName} (attempt {Attempt}/{MaxRetries})",
                    fileName, attempt, MaxRetries);
            } catch (IOException ex) {
                _logger.LogError(
                    ex,
                    "IO error processing data set {FileName} (attempt {Attempt}/{MaxRetries})",
                    fileName, attempt, MaxRetries);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                // Non-transient error — no point retrying
                _logger.LogError(
                    ex,
                    "Non-transient error processing data set {FileName}, skipping",
                    fileName);
                await ReportError("Holdings.ProcessDataSet", ex.Message, ex.StackTrace, $"file: {fileName}");
                return false;
            }
        }

        _logger.LogWarning(
            "Data set {FileName} failed all {MaxRetries} attempts — will retry at end of cycle",
            fileName, MaxRetries);
        return false;
    }

    private async Task ReportError(string context, string message, string stackTrace, string requestSummary = null) {
        try {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var errorManager = scope.ServiceProvider.GetRequiredService<ErrorManager>();
            await errorManager.Create(ErrorSource.HoldingsScraper, context, message, stackTrace, requestSummary);
        } catch { }
    }
}
