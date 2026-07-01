using System.Collections.Concurrent;
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
/// store, flips the rows to FileSystem, and deletes the FileContent rows. Progress is durable
/// in File.StorageProvider, so it resumes for free. Within a batch, blobs are loaded and
/// written by <see cref="FileBackfillOptions.Concurrency"/> parallel tasks (bounding both
/// memory and DB read parallelism); the rows commit only after a single per-batch durability
/// barrier, so no row ever points at volatile bytes. While full batches keep coming the worker
/// drains continuously (no inter-tick delay). Self-disabling: after a few consecutive empty
/// passes it stops, and since new writes now go straight to FileSystem the backlog never
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
        // Deliberately unordered: an ORDER BY would sort every remaining row on every claim
        // (millions of rows, once per batch), and drain order carries no meaning.
        var batchIds = await dbContext
            .Set<File>()
            .Where(f =>
                !(f is Image)
                && f.StorageProvider == StorageProvider.Database
                && f.FileContent != null
                && f.FileContent.Bytes != null
            )
            .Take(_backfillOptions.BatchSize)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

        if (batchIds.Count == 0)
        {
            return (0, 0);
        }

        // Phase 1 — parallel: each task loads one blob untracked in its own scope, hashes it,
        // and writes it buffered (no per-file fsync — seek-bound on a spinning disk). No
        // database state changes yet; the values to stamp on the row are collected instead.
        // Concurrency bounds both peak memory (that many blobs in flight) and DB parallelism.
        var written = new ConcurrentBag<MovedBlob>();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, _backfillOptions.Concurrency),
            CancellationToken = cancellationToken,
        };
        await Parallel.ForEachAsync(
            batchIds,
            parallelOptions,
            async (fileId, taskCancellation) =>
            {
                await using var taskScope = _scopeFactory.CreateAsyncScope();
                var taskDb =
                    taskScope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

                var blob = await taskDb
                    .Set<File>()
                    .AsNoTracking()
                    .Where(f => f.Id == fileId && f.FileContent != null)
                    .Select(f => new
                    {
                        f.Id,
                        f.ContentType,
                        ContentId = f.FileContent.Id,
                        f.FileContent.Bytes,
                    })
                    .FirstOrDefaultAsync(taskCancellation);
                if (blob?.Bytes == null)
                {
                    return;
                }

                // Audio goes to its own durability tier so a future mirrored mount at
                // <root>/audio covers the precious, hard-to-recapture recordings; everything
                // else is re-scrapable and lands on the bulk blob tier.
                var tier = IsAudio(blob.ContentType)
                    ? FileStorageTiers.Audio
                    : FileStorageTiers.Blob;
                var stamp = await fileSystemProvider.WriteBuffered(blob.Bytes, tier);
                written.Add(
                    new MovedBlob(
                        blob.Id,
                        blob.ContentId,
                        stamp.RelativePath,
                        stamp.ContentHash,
                        blob.Bytes.Length
                    )
                );
            }
        );

        if (written.IsEmpty)
        {
            return (batchIds.Count, 0);
        }

        // Batch durability barrier: flush every buffered blob to stable storage before the
        // database rows flip to FileSystem — a crash before this point leaves all rows
        // Database-stored (bytes intact in the DB), never a row pointing at volatile bytes.
        fileSystemProvider.SyncStore();

        // Phase 2 — apply the row changes via attached stubs (no bytes re-read) in a single
        // commit: flip each File to FileSystem and delete its FileContent row.
        foreach (var blob in written)
        {
            var file = new File { Id = blob.FileId };
            dbContext.Attach(file);
            file.Size = blob.Size;
            file.StorageProvider = StorageProvider.FileSystem;
            file.RelativePath = blob.RelativePath;
            file.ContentHash = blob.ContentHash;

            dbContext.Entry(new FileContent { Id = blob.ContentId, FileId = blob.FileId }).State =
                EntityState.Deleted;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "File backfill moved {Moved} blob(s) to the filesystem store",
            written.Count
        );
        return (batchIds.Count, written.Count);
    }

    private static bool IsAudio(string contentType)
    {
        return contentType != null
            && contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record MovedBlob(
        Guid FileId,
        Guid ContentId,
        string RelativePath,
        string ContentHash,
        long Size
    );
}
