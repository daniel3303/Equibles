using Equibles.Data;
using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.BusinessLogic.Storage;
using Equibles.Media.Data.Models;
using Equibles.Media.HostedService.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Media.HostedService;

/// <summary>
/// One-off, resumable drain of database-stored blobs onto the filesystem store. Each tick
/// claims a batch of Database-provider File rows (excluding small Image rows — headshots /
/// web images stay in the DB) that still hold bytes, writes them to the content-addressed
/// store, flips the row to FileSystem, and deletes the FileContent row. Progress is durable
/// in File.StorageProvider, so it resumes for free. Self-disabling: after a few consecutive
/// empty ticks it stops, and since new writes now go straight to FileSystem the backlog never
/// regrows. Runs only when both the store and the backfill are enabled in config.
/// </summary>
public class FileBackfillWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FileStorageOptions _storageOptions;
    private readonly FileBackfillOptions _backfillOptions;
    private readonly ILogger<FileBackfillWorker> _logger;

    // Virtual seams so tests can collapse the waits.
    protected virtual TimeSpan StartupDelay => TimeSpan.FromMinutes(1);
    protected virtual TimeSpan TickInterval => TimeSpan.FromSeconds(5);

    // Stop after this many consecutive empty ticks (the backlog is drained).
    private const int EmptyTicksBeforeStop = 3;

    public FileBackfillWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<FileStorageOptions> storageOptions,
        IOptions<FileBackfillOptions> backfillOptions,
        ILogger<FileBackfillWorker> logger
    )
    {
        _scopeFactory = scopeFactory;
        _storageOptions = storageOptions.Value;
        _backfillOptions = backfillOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_backfillOptions.Enabled || !_storageOptions.Enabled)
        {
            _logger.LogInformation(
                "File backfill is disabled (backfill: {Backfill}, store: {Store}); worker idle.",
                _backfillOptions.Enabled,
                _storageOptions.Enabled
            );
            return;
        }

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var emptyTicks = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            int moved;
            try
            {
                moved = await DrainOnce(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File backfill tick failed; will retry on next interval");
                moved = -1;
            }

            if (moved == 0)
            {
                emptyTicks++;
                if (emptyTicks >= EmptyTicksBeforeStop)
                {
                    _logger.LogInformation("File backfill found no more eligible rows; stopping.");
                    return;
                }
            }
            else if (moved > 0)
            {
                emptyTicks = 0;
            }

            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal async Task<int> DrainOnce(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var fileSystemProvider =
            scope.ServiceProvider.GetRequiredService<FileSystemFileStorageProvider>();

        // Claim a batch of database-stored, non-image files that still hold bytes. Image rows
        // (headshots / web images) intentionally stay in the DB; null-Bytes rows (indexed but
        // never downloaded) have nothing to move and are excluded so they don't stall the drain.
        var batch = await dbContext
            .Set<File>()
            .Where(f =>
                !(f is Image)
                && EF.Property<string>(f, nameof(File.StorageProvider)) == "Database"
                && f.FileContent != null
                && f.FileContent.Bytes != null
            )
            .OrderBy(f => f.CreationTime)
            .Take(_backfillOptions.BatchSize)
            .Include(f => f.FileContent)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            return 0;
        }

        var moved = 0;
        foreach (var file in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = file.FileContent;
            if (content?.Bytes == null)
            {
                continue;
            }

            // Write to disk first (durable), then flip the row and drop the DB bytes.
            await fileSystemProvider.Save(file, content.Bytes, FileStorageTiers.Blob);
            dbContext.Remove(content);
            moved++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "File backfill moved {Moved} blob(s) to the filesystem store",
            moved
        );
        return moved;
    }
}
