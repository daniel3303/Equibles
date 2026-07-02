using System.Text.RegularExpressions;
using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.BusinessLogic.Storage;
using Equibles.Media.Data.Models;
using Equibles.Media.HostedService.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Media.HostedService;

/// <summary>
/// Retires filesystem blobs whose File rows are gone. Inline deletion is unsafe in a
/// content-addressed store (a blob can be shared by several rows, and an unlink can race
/// an identical re-upload that dedup-skips onto the existing file), so deletion is a
/// staged, reversible pipeline instead:
///
///   1. Queue — deletes mark the blob in PendingBlobDeletion (same transaction as the row).
///   2. Sweep (daily) — for marks past the grace window, re-check every registered
///      reference checker; unreferenced blobs are RENAMED into a mirrored .trash tree
///      (atomic and reversible — a racing identical write now writes fresh bytes), then
///      references are re-checked and the blob restored if a row appeared in the gap.
///   3. Purge — trash older than the retention window is deleted permanently after one
///      final reference check (restore instead if a row reappeared).
///   4. Reconciliation (rolling) — cascades and direct repository deletes never hit the
///      queue, so each daily run also diffs one seventh of the shard space against the
///      database and trashes unreferenced blobs; the whole store is covered weekly.
///
/// Only files named as a 64-hex digest inside the store root are ever touched; temp files
/// and anything else are ignored. Runs only when the store and the sweep are enabled.
/// </summary>
public class BlobDeletionSweepWorker : BackgroundService
{
    private static readonly Regex HexName = new("^[0-9a-f]{64}$", RegexOptions.Compiled);
    private const string TrashDirectoryName = ".trash";
    private const int ReconciliationQueryChunkSize = 2000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FileStorageOptions _storageOptions;
    private readonly BlobSweepOptions _sweepOptions;
    private readonly ILogger<BlobDeletionSweepWorker> _logger;

    // Virtual seams so tests can collapse the waits.
    protected virtual TimeSpan StartupDelay => TimeSpan.FromMinutes(10);
    protected virtual TimeSpan TickInterval => TimeSpan.FromHours(24);

    public BlobDeletionSweepWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<FileStorageOptions> storageOptions,
        IOptions<BlobSweepOptions> sweepOptions,
        ILogger<BlobDeletionSweepWorker> logger
    )
    {
        _scopeFactory = scopeFactory;
        _storageOptions = storageOptions.Value;
        _sweepOptions = sweepOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_sweepOptions.Enabled || !_storageOptions.Enabled)
        {
            _logger.LogInformation(
                "Blob deletion sweep is disabled (sweep: {Sweep}, store: {Store}); worker idle.",
                _sweepOptions.Enabled,
                _storageOptions.Enabled
            );
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(StartupDelay, stoppingToken);
                await SweepOnce(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Blob deletion sweep failed; will retry on next interval");
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

    internal async Task<(int Trashed, int Purged, int Restored)> SweepOnce(
        CancellationToken cancellationToken
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext =
            scope.ServiceProvider.GetRequiredService<Equibles.Data.EquiblesFinancialDbContext>();
        var checkers = scope.ServiceProvider.GetServices<IBlobReferenceChecker>().ToList();
        if (checkers.Count == 0)
        {
            throw new InvalidOperationException(
                "No IBlobReferenceChecker is registered; refusing to sweep without a reference check."
            );
        }

        var root = _storageOptions.RootPath;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            _logger.LogWarning("Blob store root {Root} is missing; skipping sweep.", root);
            return (0, 0, 0);
        }

        var now = DateTime.UtcNow;
        var trashed = await SweepQueue(dbContext, checkers, root, now, cancellationToken);
        var (purged, restored) = await PurgeTrash(checkers, root, now, cancellationToken);

        var reconciled = 0;
        if (_sweepOptions.ReconciliationEnabled)
        {
            reconciled = await ReconcileShards(checkers, root, now, cancellationToken);
        }

        _logger.LogInformation(
            "Blob sweep done: {Trashed} queued blob(s) trashed, {Reconciled} reconciled "
                + "blob(s) trashed, {Purged} purged, {Restored} restored",
            trashed,
            reconciled,
            purged,
            restored
        );
        return (trashed + reconciled, purged, restored);
    }

    // Phase 1+2: process queued deletions past the grace window.
    private async Task<int> SweepQueue(
        Equibles.Data.EquiblesFinancialDbContext dbContext,
        IReadOnlyList<IBlobReferenceChecker> checkers,
        string root,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var cutoff = now.AddHours(-_sweepOptions.GraceHours);
        var due = await dbContext
            .Set<PendingBlobDeletion>()
            .Where(p => p.QueuedAt < cutoff)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        var trashed = 0;
        // Duplicate queue rows for the same hash collapse into one decision.
        foreach (var group in due.GroupBy(p => p.ContentHash))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = group.First().RelativePath;
            if (
                !await IsReferencedByAny(checkers, group.Key, cancellationToken)
                && TryTrashBlob(root, relativePath, now)
            )
            {
                // A row may have committed between the check and the rename (an identical
                // re-upload that dedup-skipped just before the blob moved). The rename made
                // later writes self-sufficient; restore covers the one that raced us.
                if (await IsReferencedByAny(checkers, group.Key, cancellationToken))
                {
                    RestoreBlob(root, relativePath);
                }
                else
                {
                    trashed++;
                }
            }

            dbContext.RemoveRange(group);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return trashed;
    }

    // Phase 3: permanently delete trash past retention, restoring anything re-referenced.
    private async Task<(int Purged, int Restored)> PurgeTrash(
        IReadOnlyList<IBlobReferenceChecker> checkers,
        string root,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var trashRoot = Path.Combine(root, TrashDirectoryName);
        if (!Directory.Exists(trashRoot))
        {
            return (0, 0);
        }

        var purged = 0;
        var restored = 0;
        var cutoff = now.AddHours(-_sweepOptions.TrashRetentionHours);
        foreach (
            var trashPath in Directory.EnumerateFiles(trashRoot, "*", SearchOption.AllDirectories)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileName(trashPath);
            if (!HexName.IsMatch(name) || System.IO.File.GetLastWriteTimeUtc(trashPath) >= cutoff)
            {
                continue;
            }

            if (
                await IsReferencedByAny(
                    checkers,
                    ContentAddressedPath.HashPrefix + name,
                    cancellationToken
                )
            )
            {
                RestoreBlob(root, Path.GetRelativePath(trashRoot, trashPath));
                restored++;
            }
            else
            {
                System.IO.File.Delete(trashPath);
                purged++;
            }
        }

        return (purged, restored);
    }

    // Phase 4: rolling disk-vs-database diff over one seventh of the shard space per run,
    // catching deletions that never hit the queue (cascades, direct repository deletes).
    private async Task<int> ReconcileShards(
        IReadOnlyList<IBlobReferenceChecker> checkers,
        string root,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var graceCutoff = now.AddHours(-_sweepOptions.GraceHours);
        var todaySlot = (int)now.DayOfWeek;
        var trashed = 0;

        foreach (var tier in new[] { FileStorageTiers.Blob, FileStorageTiers.Audio })
        {
            var algorithmRoot = Path.Combine(root, tier, ContentAddressedPath.Algorithm);
            if (!Directory.Exists(algorithmRoot))
            {
                continue;
            }

            foreach (var shardDir in Directory.EnumerateDirectories(algorithmRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var shard = Path.GetFileName(shardDir);
                if (
                    shard.Length != 2
                    || !int.TryParse(
                        shard,
                        System.Globalization.NumberStyles.HexNumber,
                        null,
                        out var shardByte
                    )
                    || shardByte % 7 != todaySlot
                )
                {
                    continue;
                }

                // Old enough that any owning row has long committed; young files are skipped.
                var candidates = Directory
                    .EnumerateFiles(shardDir, "*", SearchOption.AllDirectories)
                    .Where(p =>
                        HexName.IsMatch(Path.GetFileName(p))
                        && System.IO.File.GetLastWriteTimeUtc(p) < graceCutoff
                    )
                    .ToList();

                foreach (var chunk in candidates.Chunk(ReconciliationQueryChunkSize))
                {
                    var byHash = chunk.ToDictionary(
                        p => ContentAddressedPath.HashPrefix + Path.GetFileName(p),
                        p => p
                    );
                    var referenced = await GetReferencedByAny(
                        checkers,
                        byHash.Keys.ToList(),
                        cancellationToken
                    );

                    foreach (var (hash, fullPath) in byHash)
                    {
                        if (referenced.Contains(hash))
                        {
                            continue;
                        }

                        var relativePath = Path.GetRelativePath(root, fullPath)
                            .Replace(Path.DirectorySeparatorChar, '/');
                        if (TryTrashBlob(root, relativePath, now))
                        {
                            if (await IsReferencedByAny(checkers, hash, cancellationToken))
                            {
                                RestoreBlob(root, relativePath);
                            }
                            else
                            {
                                trashed++;
                            }
                        }
                    }
                }
            }
        }

        return trashed;
    }

    private static async Task<bool> IsReferencedByAny(
        IReadOnlyList<IBlobReferenceChecker> checkers,
        string contentHash,
        CancellationToken cancellationToken
    )
    {
        foreach (var checker in checkers)
        {
            if (await checker.IsReferenced(contentHash, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<HashSet<string>> GetReferencedByAny(
        IReadOnlyList<IBlobReferenceChecker> checkers,
        IReadOnlyCollection<string> contentHashes,
        CancellationToken cancellationToken
    )
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var checker in checkers)
        {
            referenced.UnionWith(await checker.GetReferenced(contentHashes, cancellationToken));
        }

        return referenced;
    }

    /// <summary>
    /// Atomically moves a blob into the mirrored trash tree and stamps its trash time.
    /// Returns false when the blob is already gone (queued twice, or removed earlier).
    /// </summary>
    private bool TryTrashBlob(string root, string relativePath, DateTime now)
    {
        var source = Path.Combine(root, ContentAddressedPath.ToOsPath(relativePath));
        if (!System.IO.File.Exists(source))
        {
            return false;
        }

        var target = Path.Combine(
            root,
            TrashDirectoryName,
            ContentAddressedPath.ToOsPath(relativePath)
        );
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        try
        {
            System.IO.File.Move(source, target, overwrite: true);
            // The rename preserves the original write time; the purge ages trash entries
            // by when they were trashed, so restamp.
            System.IO.File.SetLastWriteTimeUtc(target, now);
            return true;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(
                ex,
                "Could not trash blob {RelativePath}; leaving in place",
                relativePath
            );
            return false;
        }
    }

    /// <summary>
    /// Moves a trashed blob back to its live path. If an identical blob was re-created in
    /// the meantime the trash copy is redundant (same bytes by content addressing) and is
    /// deleted instead.
    /// </summary>
    private void RestoreBlob(string root, string relativePath)
    {
        var trashPath = Path.Combine(
            root,
            TrashDirectoryName,
            ContentAddressedPath.ToOsPath(relativePath)
        );
        var livePath = Path.Combine(root, ContentAddressedPath.ToOsPath(relativePath));
        if (!System.IO.File.Exists(trashPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(livePath));
        try
        {
            System.IO.File.Move(trashPath, livePath, overwrite: false);
            _logger.LogInformation("Restored re-referenced blob {RelativePath}", relativePath);
        }
        catch (IOException) when (System.IO.File.Exists(livePath))
        {
            // An identical write already re-created the live path; the trash copy is redundant.
            System.IO.File.Delete(trashPath);
        }
    }
}
