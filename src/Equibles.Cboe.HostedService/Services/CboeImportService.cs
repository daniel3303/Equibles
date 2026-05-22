using Equibles.Cboe.Data.Models;
using Equibles.Cboe.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Cboe.Contracts;
using Equibles.Integrations.Cboe.Models;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Cboe.HostedService.Services;

[Service]
public class CboeImportService
{
    private const int InsertBatchSize = 1000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CboeImportService> _logger;
    private readonly ICboeClient _cboeClient;
    private readonly ErrorReporter _errorReporter;

    public CboeImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<CboeImportService> logger,
        ICboeClient cboeClient,
        ErrorReporter errorReporter
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _cboeClient = cboeClient;
        _errorReporter = errorReporter;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        await ImportAllPutCallRatios(cancellationToken);
        await ImportVixHistory(cancellationToken);
    }

    private async Task ImportAllPutCallRatios(CancellationToken cancellationToken)
    {
        var typeMapping = new Dictionary<CboePutCallCsvType, CboePutCallRatioType>
        {
            [CboePutCallCsvType.Total] = CboePutCallRatioType.Total,
            [CboePutCallCsvType.Equity] = CboePutCallRatioType.Equity,
            [CboePutCallCsvType.Index] = CboePutCallRatioType.Index,
            [CboePutCallCsvType.Vix] = CboePutCallRatioType.Vix,
            [CboePutCallCsvType.Etp] = CboePutCallRatioType.Etp,
        };

        foreach (var (csvType, ratioType) in typeMapping)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ImportPutCallRatio(csvType, ratioType, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to download CBOE {Type} put/call ratio CSV, skipping",
                    csvType
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing CBOE {Type} put/call ratios", csvType);
                await _errorReporter.Report(
                    ErrorSource.CboeScraper,
                    "CboeImport.ImportPutCallRatio",
                    ex.Message,
                    ex.StackTrace,
                    $"type: {csvType}"
                );
            }
        }
    }

    private async Task ImportPutCallRatio(
        CboePutCallCsvType csvType,
        CboePutCallRatioType ratioType,
        CancellationToken cancellationToken
    )
    {
        var records = await _cboeClient.DownloadPutCallRatios(csvType);
        _logger.LogDebug(
            "CBOE {Type} put/call: downloaded {Count} records",
            csvType,
            records.Count
        );

        if (records.Count == 0)
            return;

        DateOnly latestStoredDate;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<CboePutCallRatioRepository>();
            latestStoredDate = await repo.GetLatestDate(ratioType)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var newRecords =
            latestStoredDate != default
                ? records.Where(r => r.Date > latestStoredDate).ToList()
                : records;

        if (newRecords.Count == 0)
        {
            _logger.LogDebug("CBOE {Type} put/call ratios are up to date", csvType);
            return;
        }

        var totalInserted = await BatchPersister.Persist(
            newRecords.Select(r => new CboePutCallRatio
            {
                RatioType = ratioType,
                Date = r.Date,
                CallVolume = r.CallVolume,
                PutVolume = r.PutVolume,
                TotalVolume = r.TotalVolume,
                PutCallRatio = r.PutCallRatio,
            }),
            InsertBatchSize,
            FlushBatch<CboePutCallRatio, CboePutCallRatioRepository>
        );

        _logger.LogInformation(
            "CBOE {Type} put/call: imported {Count} new records",
            csvType,
            totalInserted
        );
    }

    private async Task ImportVixHistory(CancellationToken cancellationToken)
    {
        try
        {
            var records = await _cboeClient.DownloadVixHistory();
            _logger.LogDebug("CBOE VIX: downloaded {Count} records", records.Count);

            if (records.Count == 0)
                return;

            DateOnly latestStoredDate;
            using (var scope = _scopeFactory.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<CboeVixDailyRepository>();
                latestStoredDate = await repo.GetLatestDate()
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var newRecords =
                latestStoredDate != default
                    ? records.Where(r => r.Date > latestStoredDate).ToList()
                    : records;

            if (newRecords.Count == 0)
            {
                _logger.LogDebug("CBOE VIX history is up to date");
                return;
            }

            var totalInserted = await BatchPersister.Persist(
                newRecords.Select(r => new CboeVixDaily
                {
                    Date = r.Date,
                    Open = r.Open,
                    High = r.High,
                    Low = r.Low,
                    Close = r.Close,
                }),
                InsertBatchSize,
                FlushBatch<CboeVixDaily, CboeVixDailyRepository>
            );

            _logger.LogInformation("CBOE VIX: imported {Count} new daily records", totalInserted);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to download CBOE VIX history CSV, skipping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing CBOE VIX history");
            await _errorReporter.Report(
                ErrorSource.CboeScraper,
                "CboeImport.ImportVixHistory",
                ex.Message,
                ex.StackTrace
            );
        }
    }

    private async Task FlushBatch<TEntity, TRepository>(List<TEntity> items)
        where TEntity : class
        where TRepository : BaseRepository<TEntity>
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<TRepository>();
        repo.AddRange(items);
        await repo.SaveChanges();
    }
}
