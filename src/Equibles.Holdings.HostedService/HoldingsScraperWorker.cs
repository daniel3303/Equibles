using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
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

        await BackfillProcessedDataSets(fileNames, stoppingToken);

        Logger.LogInformation(
            "Processing {Count} quarterly data sets from {Start:yyyy-MM-dd}",
            fileNames.Count, startDate);

        var failedDataSets = new List<string>();

        foreach (var fileName in fileNames) {
            stoppingToken.ThrowIfCancellationRequested();

            if (await IsAlreadyProcessed(fileName)) {
                Logger.LogDebug("Skipping already-processed data set: {FileName}", fileName);
                continue;
            }

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

        // Recalculate holdings that were imported without a Yahoo price available
        await RecalculatePendingValues(stoppingToken);
    }

    /// <summary>
    /// On first run (empty ProcessedDataSet table), seeds all file names except the latest
    /// so only the most recent period gets downloaded and checked.
    /// </summary>
    private async Task BackfillProcessedDataSets(List<string> fileNames, CancellationToken cancellationToken) {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ProcessedDataSetRepository>();

        if (await repo.GetAll().AnyAsync(cancellationToken)) return;
        if (fileNames.Count <= 1) return;

        // Seed all except the last file name (most recent period)
        var toSeed = fileNames.Take(fileNames.Count - 1);
        foreach (var fileName in toSeed) {
            repo.Add(new Holdings.Data.Models.ProcessedDataSet { FileName = fileName });
        }

        await repo.SaveChanges();
        Logger.LogInformation(
            "Backfilled {Count} historical data sets as already processed",
            fileNames.Count - 1);
    }

    private async Task<bool> IsAlreadyProcessed(string fileName) {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ProcessedDataSetRepository>();
        return await repo.Exists(fileName);
    }

    private async Task MarkAsProcessed(string fileName, int submissionCount) {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ProcessedDataSetRepository>();
        repo.Add(new Holdings.Data.Models.ProcessedDataSet {
            FileName = fileName,
            SubmissionCount = submissionCount,
        });
        await repo.SaveChanges();
    }

    private async Task RecalculatePendingValues(CancellationToken cancellationToken) {
        try {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var recalculator = scope.ServiceProvider.GetRequiredService<HoldingsValueRecalculator>();
            await recalculator.Recalculate(cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            Logger.LogError(ex, "Failed to recalculate pending holding values");
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

                await using var scope = ScopeFactory.CreateAsyncScope();
                var dataSetClient = scope.ServiceProvider.GetRequiredService<HoldingsDataSetClient>();
                var importService = scope.ServiceProvider.GetRequiredService<HoldingsImportService>();

                using var archive = await dataSetClient.DownloadDataSet(fileName, cancellationToken);
                var result = await importService.ImportDataSet(archive, minReportDate, cancellationToken);

                if (result.IsComplete) {
                    try {
                        await MarkAsProcessed(fileName, result.SubmissionCount);
                    } catch (Exception ex) {
                        Logger.LogError(ex,
                            "Failed to mark data set {FileName} as processed (import was successful)",
                            fileName);
                    }
                } else {
                    Logger.LogWarning(
                        "Data set {FileName} import incomplete (structural issue), will retry next cycle",
                        fileName);
                }

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
