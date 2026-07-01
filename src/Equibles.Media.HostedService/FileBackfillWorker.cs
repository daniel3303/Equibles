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
/// One-off, resumable drain of database-stored blobs onto the filesystem store. Each pass
/// claims a batch of Database-provider File rows (excluding small Image rows — headshots /
/// web images stay in the DB) that still hold bytes, writes them to the content-addressed
/// store, flips the row to FileSystem, and deletes the FileContent row. Progress is durable
/// in File.StorageProvider, so it resumes for free. While full batches keep coming the worker
/// drains continuously (no inter-tick delay); memory stays bounded because blobs are loaded
/// one at a time and released as soon as they are written. Self-disabling: after a few
/// consecutive empty passes it stops, and since new writes now go straight to FileSystem the
/// backlog never regrows. Runs only when both the store and the backfill are enabled in config.
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

    // Stop after this many consecutive empty passes (the backlog is drained).
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
            var claimed = -1;
            try
            {
                (claimed, _) = await DrainOnce(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File backfill tick failed; will retry on next interval");
            }

            if (claimed == 0)
            {
                emptyTicks++;
                if (emptyTicks >= EmptyTicksBeforeStop)
                {
                    _logger.LogInformation("File backfill found no more eligible rows; stopping.");
                    return;
                }
            }
            else if (claimed > 0)
            {
                emptyTicks = 0;

                // A full batch means the backlog almost certainly continues — drain
                // straight through without sleeping so throughput is bounded by I/O,
                // not by the tick interval.
                if (claimed >= _backfillOptions.BatchSize)
                {
                    continue;
                }
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

    internal async Task<(int Claimed, int Moved)> DrainOnce(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var fileSystemProvider =
            scope.ServiceProvider.GetRequiredService<FileSystemFileStorageProvider>();

        // Claim a batch of database-stored, non-image files that still hold bytes. Image rows
        // (headshots / web images) intentionally stay in the DB; null-Bytes rows (indexed but
        // never downloaded) have nothing to move and are excluded so they don't stall the drain.
        // Only ids are claimed here — bytes are loaded one file at a time below so peak memory
        // is a single blob regardless of batch size (audio blobs run tens of MB each).
        var batchIds = await dbContext
            .Set<File>()
            .Where(f =>
                !(f is Image)
                && f.StorageProvider == StorageProvider.Database
                && f.FileContent != null
                && f.FileContent.Bytes != null
            )
            .OrderBy(f => f.CreationTime)
            .Take(_backfillOptions.BatchSize)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

        if (batchIds.Count == 0)
        {
            return (0, 0);
        }

        var moved = 0;
        foreach (var fileId in batchIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = await dbContext
                .Set<File>()
                .Include(f => f.FileContent)
                .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
            var content = file?.FileContent;
            if (content?.Bytes == null)
            {
                continue;
            }

            // Write to disk first (durable), then flip the row and drop the DB bytes.
            // Audio goes to its own durability tier so a future mirrored mount at
            // <root>/audio covers the precious, hard-to-recapture recordings; everything
            // else is re-scrapable and lands on the bulk blob tier.
            var tier = IsAudio(file) ? FileStorageTiers.Audio : FileStorageTiers.Blob;
            await fileSystemProvider.Save(file, content.Bytes, tier);
            dbContext.Remove(content);

            // Release the blob reference immediately — the row is being deleted, and the
            // tracked entity would otherwise pin every batch blob in memory until SaveChanges.
            content.Bytes = null;
            moved++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "File backfill moved {Moved} blob(s) to the filesystem store",
            moved
        );
        return (batchIds.Count, moved);
    }

    private static bool IsAudio(File file)
    {
        return file.ContentType != null
            && file.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
    }
}
