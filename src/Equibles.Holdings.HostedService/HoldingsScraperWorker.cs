using Equibles.Core;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Equibles.Holdings.HostedService;

public class HoldingsScraperWorker : BaseScraperWorker {
    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays = [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
    ];

    private readonly WorkerOptions _workerOptions;
    private readonly IConfiguration _configuration;

    protected override string WorkerName => "Holdings scraper";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(24);
    protected override ErrorSource ErrorSource => ErrorSource.HoldingsScraper;

    public HoldingsScraperWorker(
        ILogger<HoldingsScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<WorkerOptions> workerOptions,
        IConfiguration configuration
    ) : base(logger, scopeFactory, errorReporter) {
        _workerOptions = workerOptions.Value;
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration() {
        if (string.IsNullOrEmpty(_configuration["Sec:ContactEmail"])) {
            Logger.LogWarning("Holdings Scraper stopped: SEC_CONTACT_EMAIL not configured. Set it in your .env file.");
            return false;
        }
        return true;
    }

    protected override async Task DoWork(CancellationToken stoppingToken) {
        var startDate = _workerOptions.MinSyncDate ?? new DateTime(2020, 1, 1);
        var minReportDate = DateOnly.FromDateTime(startDate);
        var fileNames = HoldingsDataSetClient.GetDataSetFileNames(startDate);

        Logger.LogInformation(
            "Processing {Count} quarterly data sets from {Start:yyyy-MM-dd}",
            fileNames.Count, startDate);

        var failedDataSets = new List<string>();

        foreach (var fileName in fileNames) {
            stoppingToken.ThrowIfCancellationRequested();

            if (!await TryProcessDataSet(fileName, minReportDate, stoppingToken)) {
                failedDataSets.Add(fileName);
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        if (failedDataSets.Count > 0) {
            Logger.LogWarning(
                "Retrying {Count} failed data sets at end of cycle: {FileNames}",
                failedDataSets.Count, string.Join(", ", failedDataSets));

            foreach (var fileName in failedDataSets) {
                stoppingToken.ThrowIfCancellationRequested();

                if (!await TryProcessDataSet(fileName, minReportDate, stoppingToken)) {
                    Logger.LogError(
                        "Data set {FileName} permanently failed after all retry attempts in this cycle",
                        fileName);
                    await ErrorReporter.Report(ErrorSource, "Holdings.ProcessDataSet",
                        "Permanently failed after all retry attempts", null,
                        $"file: {fileName}, permanently failed");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }
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
                    Logger.LogInformation(
                        "Retry attempt {Attempt}/{MaxRetries} for {FileName} after {Delay}s",
                        attempt, MaxRetries, fileName, delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken);
                }

                Logger.LogInformation("Processing data set: {FileName}", fileName);

                using var scope = ScopeFactory.CreateScope();
                var dataSetClient = scope.ServiceProvider.GetRequiredService<HoldingsDataSetClient>();
                var importService = scope.ServiceProvider.GetRequiredService<HoldingsImportService>();

                var valueInThousands = HoldingsDataSetClient.IsValueInThousands(fileName);
                using var archive = await dataSetClient.DownloadDataSet(fileName, cancellationToken);
                await importService.ImportDataSet(archive, minReportDate, valueInThousands, cancellationToken);

                GarbageCollectorUtil.ForceAggressiveCollection();

                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                return true;
            } catch (HttpRequestException ex) {
                Logger.LogError(
                    ex,
                    "Failed to download data set {FileName} (attempt {Attempt}/{MaxRetries})",
                    fileName, attempt, MaxRetries);
            } catch (IOException ex) {
                Logger.LogError(
                    ex,
                    "IO error processing data set {FileName} (attempt {Attempt}/{MaxRetries})",
                    fileName, attempt, MaxRetries);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Logger.LogError(
                    ex,
                    "Non-transient error processing data set {FileName}, skipping",
                    fileName);
                await ErrorReporter.Report(ErrorSource, "Holdings.ProcessDataSet",
                    ex.Message, ex.StackTrace, $"file: {fileName}");
                return false;
            }
        }

        Logger.LogWarning(
            "Data set {FileName} failed all {MaxRetries} attempts — will retry at end of cycle",
            fileName, MaxRetries);
        return false;
    }
}
