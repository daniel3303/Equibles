using System.Text.RegularExpressions;
using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.BusinessLogic.Storage;
using Equibles.Media.Data.Models;
using Equibles.Media.HostedService.Configuration;
using Equibles.Media.HostedService.Models;
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
///   2. Sweep (every start and daily) — for marks past the grace window, re-check every registered
///      reference checker; unreferenced blobs are RENAMED into a mirrored .trash tree
///      (atomic and reversible — a racing identical write now writes fresh bytes), then
///      references are re-checked and the blob restored if a row appeared in the gap.
///   3. Purge — trash older than the retention window is deleted permanently after one
///      final reference check (restore instead if a row reappeared).
///   4. Reconciliation (rolling) — cascades and direct repository deletes never hit the
///      queue, so each run also diffs one seventh of the shard space against the database
///      and trashes unreferenced blobs; seven completed runs cover the whole store. Only an
///      unreferenced file's timestamp is read: on a busy spinning disk each read costs a seek,
///      and reading every file's made a slice take days instead of hours.
///
/// Progress is saved in the store root (<see cref="BlobSweepStateStore"/>): a worker restart
/// inside the interval starts no new run, and an interrupted run resumes after the last folder
/// it finished, since a walk of the whole store on a spinning disk outlasts the gap between
/// deploys. Only files named as a 64-hex digest inside the store root are ever touched; temp
/// files and anything else are ignored. Runs only when the store and the sweep are enabled.
/// </summary>
public class BlobDeletionSweepWorker : BackgroundService
{
    private static readonly Regex HexName = new("^[0-9a-f]{64}$", RegexOptions.Compiled);
    private const string TrashDirectoryName = ".trash";
    private const int ReconciliationQueryChunkSize = 2000;
    private const int ReconciliationSlots = 7;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FileStorageOptions _storageOptions;
    private readonly BlobSweepOptions _sweepOptions;
    private readonly ILogger<BlobDeletionSweepWorker> _logger;

    // Virtual seams so tests can collapse the waits.
    protected virtual TimeSpan StartupDelay => TimeSpan.FromMinutes(10);
    protected virtual TimeSpan TickInterval => TimeSpan.FromHours(24);
    protected virtual TimeSpan FailureRetryDelay => TimeSpan.FromHours(1);
    protected virtual TimeSpan CheckpointInterval => TimeSpan.FromSeconds(30);

    // A misconfigured tiny grace (e.g. 0) would let the sweep race blobs whose rows are
    // still inside an uncommitted write window, causing hours-long read outages until the
    // purge restores them. Clamp to a floor no configuration can go below.
    internal const int MinimumGraceHours = 6;

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

    private int EffectiveGraceHours => Math.Max(_sweepOptions.GraceHours, MinimumGraceHours);

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

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TickInterval;
            try
            {
                var root = ExistingStoreRoot();
                if (root != null)
                {
                    var due = NextRunDelay(
                        new BlobSweepStateStore(root, _logger).Load(),
                        DateTime.UtcNow
                    );
                    if (due > TimeSpan.Zero)
                    {
                        delay = due;
                    }
                    else
                    {
                        await SweepOnce(stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Blob deletion sweep failed; the next attempt resumes its saved progress"
                );
                delay = FailureRetryDelay;
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Zero while a run is unfinished or none ever completed, otherwise what is left of the
    /// interval since the last completion; never more than one interval, so a clock that
    /// moved back cannot park the sweep.
    /// </summary>
    internal TimeSpan NextRunDelay(BlobSweepState state, DateTime now)
    {
        if (state.Run != null || state.LastCompletedAt is not { } completedAt)
        {
            return TimeSpan.Zero;
        }

        var remaining = completedAt + TickInterval - now;
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return remaining < TickInterval ? remaining : TickInterval;
    }

    private string ExistingStoreRoot()
    {
        var root = _storageOptions.RootPath;
        if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
        {
            return root;
        }

        _logger.LogWarning("Blob store root {Root} is missing; skipping sweep.", root);
        return null;
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

        var root = ExistingStoreRoot();
        if (root == null)
        {
            return (0, 0, 0);
        }

        var now = DateTime.UtcNow;

        // The queue is a quick database pass, so it runs on every start rather than waiting
        // behind a walk that keeps failing.
        var trashed = await SweepQueue(dbContext, checkers, root, now, cancellationToken);

        var stateStore = new BlobSweepStateStore(root, _logger);
        var state = stateStore.Load();
        if (state.Run == null)
        {
            state.Run = new BlobSweepRun
            {
                StartedAt = now,
                Slot = state.LastSlot is { } lastSlot
                    ? (lastSlot + 1) % ReconciliationSlots
                    : (int)now.DayOfWeek,
                Phase = BlobSweepPhase.Purge,
            };
            _logger.LogInformation(
                "Blob sweep starting a run on reconciliation slot {Slot}",
                state.Run.Slot
            );
        }
        else
        {
            _logger.LogInformation(
                "Blob sweep resuming the run started {StartedAt:o} at {Phase} after {Position}",
                state.Run.StartedAt,
                state.Run.Phase,
                state.Run.Position ?? "(start)"
            );
        }

        var progress = new BlobSweepProgress(stateStore, state, CheckpointInterval);
        progress.Save();

        var reconciled = 0;
        var purged = 0;
        var restored = 0;
        try
        {
            if (progress.Run.Phase == BlobSweepPhase.Purge)
            {
                (purged, restored) = await PurgeTrash(
                    checkers,
                    root,
                    now,
                    progress,
                    cancellationToken
                );
                progress.StartPhase(BlobSweepPhase.Reconcile);
            }

            if (_sweepOptions.ReconciliationEnabled)
            {
                reconciled = await ReconcileShards(
                    checkers,
                    root,
                    now,
                    progress,
                    cancellationToken
                );
            }
        }
        catch
        {
            TrySave(progress);
            throw;
        }

        progress.Complete();

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

    private void TrySave(BlobSweepProgress progress)
    {
        try
        {
            progress.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the blob sweep's progress");
        }
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
        var cutoff = now.AddHours(-EffectiveGraceHours);
        var due = await dbContext
            .Set<PendingBlobDeletion>()
            .Where(p => p.QueuedAt < cutoff)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        var trashed = 0;
        // Duplicate queue rows for the same hash collapse into one reference decision;
        // every distinct path is still processed, since identical bytes could in
        // principle have been stored under more than one tier.
        foreach (var group in due.GroupBy(p => p.ContentHash))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await IsReferencedByAny(checkers, group.Key, cancellationToken))
            {
                foreach (var relativePath in group.Select(p => p.RelativePath).Distinct())
                {
                    if (!TryTrashBlob(root, relativePath, now))
                    {
                        continue;
                    }

                    // A row may have committed between the check and the rename (an
                    // identical re-upload that dedup-skipped just before the blob moved).
                    // The rename made later writes self-sufficient; restore covers the
                    // one that raced us.
                    if (await IsReferencedByAny(checkers, group.Key, cancellationToken))
                    {
                        RestoreBlob(root, relativePath);
                    }
                    else
                    {
                        trashed++;
                    }
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
        BlobSweepProgress progress,
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
        await SortedDirectoryWalk.Walk(
            trashRoot,
            progress.Run.Position,
            _ => true,
            async (directory, folder) =>
            {
                foreach (var trashPath in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var name = Path.GetFileName(trashPath);
                    if (
                        !HexName.IsMatch(name)
                        || System.IO.File.GetLastWriteTimeUtc(trashPath) >= cutoff
                    )
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

                // Emptied folders go, so later walks list only populated ones; trashing a blob
                // recreates its folder.
                if (folder.Count > 0)
                {
                    TryDeleteEmptyDirectory(directory);
                }

                progress.Finished(folder);
            },
            cancellationToken
        );

        return (purged, restored);
    }

    // Phase 4: rolling disk-vs-database diff over one seventh of the shard space per run,
    // catching deletions that never hit the queue (cascades, direct repository deletes).
    private async Task<int> ReconcileShards(
        IReadOnlyList<IBlobReferenceChecker> checkers,
        string root,
        DateTime now,
        BlobSweepProgress progress,
        CancellationToken cancellationToken
    )
    {
        var graceCutoff = now.AddHours(-EffectiveGraceHours);
        var slot = progress.Run.Slot;
        var trashed = 0;
        var pending = new List<string>();

        var tiers = _sweepOptions.ReconciliationIncludesAudioTier
            ? new[] { FileStorageTiers.Blob, FileStorageTiers.Audio }
            : new[] { FileStorageTiers.Blob };
        await SortedDirectoryWalk.Walk(
            root,
            progress.Run.Position,
            folder => IsReconciledFolder(folder, tiers, slot),
            async (directory, folder) =>
            {
                // Blobs sit at or below <tier>/<algorithm>/<shard>. Listing reads only the
                // folder; a file's timestamp is read later, and only if no row references it.
                if (folder.Count >= 3)
                {
                    pending.AddRange(
                        Directory
                            .EnumerateFiles(directory)
                            .Where(p => HexName.IsMatch(Path.GetFileName(p)))
                    );
                }

                // Folders share one reference query; a folder becomes the position only once
                // its files' batch is done. The root is visited last, so it settles the rest.
                if (pending.Count >= ReconciliationQueryChunkSize || folder.Count == 0)
                {
                    trashed += await ReconcileCandidates(
                        checkers,
                        root,
                        pending,
                        now,
                        graceCutoff,
                        cancellationToken
                    );
                    pending.Clear();
                }

                if (pending.Count == 0)
                {
                    progress.Finished(folder);
                }
            },
            cancellationToken
        );

        return trashed;
    }

    private static bool IsReconciledFolder(
        IReadOnlyList<string> folder,
        IReadOnlyCollection<string> tiers,
        int slot
    )
    {
        return folder.Count switch
        {
            1 => tiers.Contains(folder[0]),
            2 => folder[1] == ContentAddressedPath.Algorithm,
            3 => folder[2].Length == 2
                && int.TryParse(
                    folder[2],
                    System.Globalization.NumberStyles.HexNumber,
                    null,
                    out var shardByte
                )
                && shardByte % ReconciliationSlots == slot,
            _ => true,
        };
    }

    private async Task<int> ReconcileCandidates(
        IReadOnlyList<IBlobReferenceChecker> checkers,
        string root,
        IReadOnlyList<string> candidates,
        DateTime now,
        DateTime graceCutoff,
        CancellationToken cancellationToken
    )
    {
        var trashed = 0;
        foreach (var chunk in candidates.Chunk(ReconciliationQueryChunkSize))
        {
            // Two tiers can hold the same bytes, so a hash may name more than one path.
            var referenced = await GetReferencedByAny(
                checkers,
                chunk.Select(HashOf).Distinct().ToList(),
                cancellationToken
            );

            foreach (var fullPath in chunk)
            {
                var hash = HashOf(fullPath);
                // Old enough that any owning row has long committed; young files are skipped.
                if (
                    referenced.Contains(hash)
                    || System.IO.File.GetLastWriteTimeUtc(fullPath) >= graceCutoff
                )
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

        return trashed;
    }

    private static string HashOf(string blobPath) =>
        ContentAddressedPath.HashPrefix + Path.GetFileName(blobPath);

    private static void TryDeleteEmptyDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still holds a file or folder; only an empty one is removed.
        }
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
    /// Rejects any relative path that resolves outside the store root or does not name a
    /// 64-hex digest. Queue paths are self-produced, but this worker moves and deletes
    /// files — a corrupted row must never let it reach outside the store.
    /// </summary>
    private bool IsSafeStorePath(string root, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath) || !HexName.IsMatch(Path.GetFileName(relativePath)))
        {
            return false;
        }

        var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(
            Path.Combine(root, ContentAddressedPath.ToOsPath(relativePath))
        );
        if (!resolved.StartsWith(rootFull, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Refusing to touch blob path {RelativePath} outside the store root",
                relativePath
            );
            return false;
        }

        return true;
    }

    /// <summary>
    /// Atomically moves a blob into the mirrored trash tree and stamps its trash time.
    /// Returns false when the blob is already gone (queued twice, or removed earlier).
    /// </summary>
    private bool TryTrashBlob(string root, string relativePath, DateTime now)
    {
        if (!IsSafeStorePath(root, relativePath))
        {
            return false;
        }

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
        if (!IsSafeStorePath(root, relativePath))
        {
            return;
        }

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
