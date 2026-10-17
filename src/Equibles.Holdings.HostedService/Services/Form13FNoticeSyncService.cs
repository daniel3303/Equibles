using System.IO.Compression;
using System.Net;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Reads the Form 13F notices of every imported 13F data set whose notices are not on file yet,
/// newest data set first. It covers new data sets as they are imported and the history alike.
/// </summary>
[Service]
public class Form13FNoticeSyncService
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly HoldingsDataSetClient _dataSets;
    private readonly TsvParser _tsvParser;
    private readonly ILogger<Form13FNoticeSyncService> _logger;

    public Form13FNoticeSyncService(
        EquiblesFinancialDbContext dbContext,
        HoldingsDataSetClient dataSets,
        TsvParser tsvParser,
        ILogger<Form13FNoticeSyncService> logger
    )
    {
        _dbContext = dbContext;
        _dataSets = dataSets;
        _tsvParser = tsvParser;
        _logger = logger;
    }

    /// <summary>
    /// Reads up to <paramref name="maxDataSets"/> of the named data sets that the holdings import
    /// has processed and the notice sync has not, and returns how many were read.
    /// </summary>
    public async Task<int> SyncMissing(
        IReadOnlyCollection<string> dataSetFileNames,
        int maxDataSets,
        CancellationToken cancellationToken
    )
    {
        // Only processed data sets are known to be published; a future window would 404 every cycle.
        var processed = await _dbContext
            .Set<ProcessedDataSet>()
            .AsNoTracking()
            .Where(dataSet => dataSetFileNames.Contains(dataSet.FileName))
            .Select(dataSet => dataSet.FileName)
            .ToListAsync(cancellationToken);
        var read = await _dbContext
            .Set<Form13FNoticeDataSet>()
            .AsNoTracking()
            .Select(dataSet => dataSet.FileName)
            .ToListAsync(cancellationToken);
        var pending = dataSetFileNames
            .Where(processed.Contains)
            .Except(read)
            .Reverse()
            .Take(maxDataSets)
            .ToList();

        var imported = 0;
        foreach (var fileName in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var archive = await _dataSets.DownloadDataSet(fileName, cancellationToken);
                await Import(archive, fileName, cancellationToken);
                imported++;
            }
            catch (Exception exception)
                when (exception is InvalidDataException
                    || exception is HttpRequestException { StatusCode: HttpStatusCode.NotFound }
                )
            {
                // One unreadable or missing archive must not hold back the others; it is retried next cycle.
                _logger.LogError(
                    exception,
                    "Could not read 13F notices from {FileName}; retrying next cycle",
                    fileName
                );
            }
        }

        return imported;
    }

    /// <summary>
    /// Stores the archive's notices, replacing any stored copy of the same accessions, and records
    /// the data set as read, all in one transaction.
    /// </summary>
    public async Task<int> Import(
        ZipArchive archive,
        string fileName,
        CancellationToken cancellationToken
    )
    {
        var notices = await Form13FNoticeReader.Read(archive, fileName, _tsvParser);
        var accessions = notices.Select(notice => notice.AccessionNumber).ToList();

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                cancellationToken
            );
            // Reporters go with their notice through the foreign key's ON DELETE CASCADE.
            await _dbContext
                .Set<Form13FNotice>()
                .Where(notice => accessions.Contains(notice.AccessionNumber))
                .ExecuteDeleteAsync(cancellationToken);
            await _dbContext
                .Set<Form13FNoticeDataSet>()
                .Where(dataSet => dataSet.FileName == fileName)
                .ExecuteDeleteAsync(cancellationToken);
            _dbContext.AddRange(notices);
            _dbContext.Add(
                new Form13FNoticeDataSet { FileName = fileName, NoticeCount = notices.Count }
            );
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        _logger.LogInformation("Read {Count} 13F notices from {FileName}", notices.Count, fileName);
        return notices.Count;
    }
}
