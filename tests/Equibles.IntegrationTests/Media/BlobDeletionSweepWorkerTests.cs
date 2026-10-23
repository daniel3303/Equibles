using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.BusinessLogic.Storage;
using Equibles.Media.Data.Models;
using Equibles.Media.HostedService;
using Equibles.Media.HostedService.Configuration;
using Equibles.Media.HostedService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Media;

/// <summary>
/// Exercises the full deletion-sweep pipeline against real Postgres and a real store
/// tree: queued blobs are trashed only when unreferenced, still-referenced queue entries
/// are dropped without touching the blob, aged trash is purged unless a row reappeared
/// (then restored), and the rolling reconciliation trashes on-disk blobs that no row
/// references even though they were never queued (the cascade-delete case). A run saves its
/// progress in the store root, so an interrupted run resumes after its last finished folder.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class BlobDeletionSweepWorkerTests : ParadeDbMcpTestBase
{
    public BlobDeletionSweepWorkerTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private sealed class TestSweepWorker : BlobDeletionSweepWorker
    {
        public TestSweepWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<FileStorageOptions> storageOptions,
            IOptions<BlobSweepOptions> sweepOptions
        )
            : base(
                scopeFactory,
                storageOptions,
                sweepOptions,
                NullLogger<BlobDeletionSweepWorker>()
            ) { }

        // Save every finished folder, so an interruption's saved position is exact.
        protected override TimeSpan CheckpointInterval => TimeSpan.Zero;
    }

    /// <summary>Answers "unreferenced" and cancels the sweep on its <c>cancelOnCall</c>-th check of either kind.</summary>
    private sealed class CountingReferenceChecker : IBlobReferenceChecker
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly int _cancelOnCall;

        public CountingReferenceChecker(
            CancellationTokenSource cancellation = null,
            int cancelOnCall = 0
        )
        {
            _cancellation = cancellation;
            _cancelOnCall = cancelOnCall;
        }

        public int Calls { get; private set; }

        public Task<bool> IsReferenced(string contentHash, CancellationToken cancellationToken)
        {
            Count(cancellationToken);
            return Task.FromResult(false);
        }

        public Task<IReadOnlySet<string>> GetReferenced(
            IReadOnlyCollection<string> contentHashes,
            CancellationToken cancellationToken
        )
        {
            Count(cancellationToken);
            return Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        }

        private void Count(CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == _cancelOnCall)
            {
                _cancellation.Cancel();
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private ServiceProvider CreateServices(IBlobReferenceChecker checker = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<EquiblesFinancialDbContext>(_ => Fixture.CreateDbContext());
        if (checker == null)
        {
            services.AddScoped<IBlobReferenceChecker, FinancialBlobReferenceChecker>();
        }
        else
        {
            services.AddScoped<IBlobReferenceChecker>(_ => checker);
        }

        return services.BuildServiceProvider();
    }

    private static TestSweepWorker CreateWorker(ServiceProvider provider, string root)
    {
        return new TestSweepWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new FileStorageOptions { Enabled = true, RootPath = root }),
            Options.Create(
                new BlobSweepOptions
                {
                    Enabled = true,
                    GraceHours = 0,
                    TrashRetentionHours = 1,
                }
            )
        );
    }

    private BlobSweepStateStore StateStore(string root) =>
        new(root, NullLogger<BlobDeletionSweepWorker>());

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "eq-sweep-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string WriteAgedTrash(string root, string shard)
    {
        var trashPath = WriteBlob(
            root,
            $".trash/blob/sha256/{shard}/00/{HexHash(shard)}",
            "trashed"u8.ToArray()
        );
        System.IO.File.SetLastWriteTimeUtc(trashPath, DateTime.UtcNow.AddHours(-2));
        return trashPath;
    }

    private static string TrashFolder(string root, string relative) =>
        Path.Combine(root, ".trash", relative.Replace('/', Path.DirectorySeparatorChar));

    private static string WriteBlob(string root, string relativePath, byte[] bytes)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        System.IO.File.WriteAllBytes(fullPath, bytes);
        return fullPath;
    }

    private static string HexHash(string firstByteHex)
    {
        return firstByteHex + new string('0', 58) + "abcd";
    }

    [Fact]
    public async Task SweepOnce_RunsTheFullQueueTrashPurgeAndReconciliationPipeline()
    {
        var root = Path.Combine(Path.GetTempPath(), "eq-sweep-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTime.UtcNow;

            // A — queued, row deleted, blob on disk → must be trashed.
            var hashA = HexHash("aa");
            var pathA = $"blob/sha256/aa/00/{hashA}";
            var fullA = WriteBlob(root, pathA, "blob-a"u8.ToArray());

            // B — queued, but a live row still references the hash (dedup / re-created)
            // → queue entry dropped, blob untouched.
            var hashB = HexHash("bb");
            var pathB = $"blob/sha256/bb/00/{hashB}";
            var fullB = WriteBlob(root, pathB, "blob-b"u8.ToArray());
            var rowB = new File
            {
                Name = "kept",
                Extension = "gz",
                ContentType = "application/gzip",
                StorageProvider = StorageProvider.FileSystem,
                ContentHash = "sha256:" + hashB,
                RelativePath = pathB,
            };

            // D — on disk, old, never queued, no row (a cascade-deleted file); its shard
            // is chosen so today's rolling reconciliation slice covers it → trashed.
            var todaySlot = (int)now.DayOfWeek;
            var shardD = Enumerable.Range(0, 256).First(b => b % 7 == todaySlot).ToString("x2");
            var hashD = HexHash(shardD);
            var pathD = $"blob/sha256/{shardD}/00/{hashD}";
            var fullD = WriteBlob(root, pathD, "blob-d"u8.ToArray());
            System.IO.File.SetLastWriteTimeUtc(fullD, now.AddDays(-3));

            // E — already in trash, aged past retention, but a live row references it
            // → the purge must RESTORE it instead of deleting.
            var hashE = HexHash("ee");
            var pathE = $"audio/sha256/ee/00/{hashE}";
            var trashE = WriteBlob(root, $".trash/{pathE}", "blob-e"u8.ToArray());
            System.IO.File.SetLastWriteTimeUtc(trashE, now.AddHours(-2));
            var rowE = new File
            {
                Name = "restored",
                Extension = "m4a",
                ContentType = "audio/mp4",
                StorageProvider = StorageProvider.FileSystem,
                ContentHash = "sha256:" + hashE,
                RelativePath = pathE,
            };

            DbContext.AddRange(rowB, rowE);
            DbContext.Add(
                new PendingBlobDeletion
                {
                    ContentHash = "sha256:" + hashA,
                    RelativePath = pathA,
                    QueuedAt = now.AddDays(-3),
                }
            );
            DbContext.Add(
                new PendingBlobDeletion
                {
                    ContentHash = "sha256:" + hashB,
                    RelativePath = pathB,
                    QueuedAt = now.AddDays(-3),
                }
            );
            await DbContext.SaveChangesAsync();
            DbContext.ChangeTracker.Clear();

            var storageOptions = Options.Create(
                new FileStorageOptions { Enabled = true, RootPath = root }
            );
            var services = new ServiceCollection();
            services.AddScoped<EquiblesFinancialDbContext>(_ => Fixture.CreateDbContext());
            services.AddScoped<IBlobReferenceChecker, FinancialBlobReferenceChecker>();
            await using var provider = services.BuildServiceProvider();

            var worker = new TestSweepWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                storageOptions,
                Options.Create(
                    new BlobSweepOptions
                    {
                        Enabled = true,
                        GraceHours = 0,
                        TrashRetentionHours = 1,
                    }
                )
            );

            await worker.SweepOnce(CancellationToken.None);

            // A: moved to trash, not yet purged (retention window).
            System.IO.File.Exists(fullA).Should().BeFalse();
            System
                .IO.File.Exists(
                    Path.Combine(root, ".trash", pathA.Replace('/', Path.DirectorySeparatorChar))
                )
                .Should()
                .BeTrue();

            // B: still live, untouched.
            System.IO.File.Exists(fullB).Should().BeTrue();

            // D: reconciliation caught the never-queued orphan.
            System.IO.File.Exists(fullD).Should().BeFalse();
            System
                .IO.File.Exists(
                    Path.Combine(root, ".trash", pathD.Replace('/', Path.DirectorySeparatorChar))
                )
                .Should()
                .BeTrue();

            // E: purge found it re-referenced and restored it to its live path.
            System
                .IO.File.Exists(Path.Combine(root, pathE.Replace('/', Path.DirectorySeparatorChar)))
                .Should()
                .BeTrue();
            System.IO.File.Exists(trashE).Should().BeFalse();

            // Queue is fully drained.
            await using var verify = Fixture.CreateDbContext();
            (await verify.Set<PendingBlobDeletion>().CountAsync()).Should().Be(0);

            // Second run: age A's trash entry past retention → permanently purged
            // (still unreferenced).
            var trashA = Path.Combine(
                root,
                ".trash",
                pathA.Replace('/', Path.DirectorySeparatorChar)
            );
            System.IO.File.SetLastWriteTimeUtc(trashA, DateTime.UtcNow.AddHours(-2));
            await worker.SweepOnce(CancellationToken.None);
            System.IO.File.Exists(trashA).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SweepOnce_SavedRunInThePurge_ResumesAfterItsPositionAndRemovesEmptiedFolders()
    {
        var root = NewRoot();
        try
        {
            var before = WriteAgedTrash(root, "b0");
            var atPosition = WriteAgedTrash(root, "b1");
            var after = WriteAgedTrash(root, "b2");
            var young = WriteBlob(
                root,
                $".trash/blob/sha256/b3/00/{HexHash("b3")}",
                "young"u8.ToArray()
            );
            Directory.CreateDirectory(TrashFolder(root, "blob/sha256/b4/00"));
            StateStore(root)
                .Save(
                    new BlobSweepState
                    {
                        LastSlot = 2,
                        Run = new BlobSweepRun
                        {
                            StartedAt = DateTime.UtcNow.AddHours(-5),
                            Slot = 3,
                            Phase = BlobSweepPhase.Purge,
                            Position = "blob/sha256/b1",
                        },
                    }
                );
            await using var provider = CreateServices();

            await CreateWorker(provider, root).SweepOnce(CancellationToken.None);

            // Folders up to the saved position were finished before the restart.
            System.IO.File.Exists(before).Should().BeTrue();
            System.IO.File.Exists(atPosition).Should().BeTrue();
            System.IO.File.Exists(after).Should().BeFalse();
            System.IO.File.Exists(young).Should().BeTrue();
            Directory.Exists(TrashFolder(root, "blob/sha256/b2")).Should().BeFalse();
            Directory.Exists(TrashFolder(root, "blob/sha256/b4")).Should().BeFalse();

            var state = StateStore(root).Load();
            state.Run.Should().BeNull();
            state.LastCompletedAt.Should().NotBeNull();
            state.LastSlot.Should().Be(3);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SweepOnce_InterruptedMidPurge_NextRunResumesAfterTheLastFinishedFolder()
    {
        var root = NewRoot();
        try
        {
            var first = WriteAgedTrash(root, "c1");
            var second = WriteAgedTrash(root, "c2");
            var third = WriteAgedTrash(root, "c3");

            using var shutdown = new CancellationTokenSource();
            var interrupted = new CountingReferenceChecker(shutdown, cancelOnCall: 2);
            await using (var provider = CreateServices(interrupted))
            {
                var sweep = () => CreateWorker(provider, root).SweepOnce(shutdown.Token);
                await sweep.Should().ThrowAsync<OperationCanceledException>();
            }

            System.IO.File.Exists(first).Should().BeFalse();
            System.IO.File.Exists(second).Should().BeTrue();
            var saved = StateStore(root).Load().Run;
            saved.Phase.Should().Be(BlobSweepPhase.Purge);
            saved.Position.Should().Be("blob/sha256/c1");

            // Trash that lands in a finished folder waits for the next run.
            var behindPosition = WriteAgedTrash(root, "c1");
            var resumed = new CountingReferenceChecker();
            await using (var provider = CreateServices(resumed))
            {
                await CreateWorker(provider, root).SweepOnce(CancellationToken.None);
            }

            resumed.Calls.Should().Be(2);
            System.IO.File.Exists(behindPosition).Should().BeTrue();
            System.IO.File.Exists(second).Should().BeFalse();
            System.IO.File.Exists(third).Should().BeFalse();
            StateStore(root).Load().Run.Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SweepOnce_AfterACompletedRun_ReconcilesTheNextSlot()
    {
        var root = NewRoot();
        try
        {
            // Shard 0x00 is slot 0, the one after slot 6; shard 0x01 is slot 1.
            var inSlot = WriteBlob(
                root,
                $"blob/sha256/00/00/{HexHash("00")}",
                "orphan-0"u8.ToArray()
            );
            var otherSlot = WriteBlob(
                root,
                $"blob/sha256/01/00/{HexHash("01")}",
                "orphan-1"u8.ToArray()
            );
            System.IO.File.SetLastWriteTimeUtc(inSlot, DateTime.UtcNow.AddDays(-3));
            System.IO.File.SetLastWriteTimeUtc(otherSlot, DateTime.UtcNow.AddDays(-3));
            StateStore(root)
                .Save(
                    new BlobSweepState
                    {
                        LastCompletedAt = DateTime.UtcNow.AddDays(-2),
                        LastSlot = 6,
                    }
                );
            await using var provider = CreateServices();

            await CreateWorker(provider, root).SweepOnce(CancellationToken.None);

            System.IO.File.Exists(inSlot).Should().BeFalse();
            System.IO.File.Exists(otherSlot).Should().BeTrue();
            StateStore(root).Load().LastSlot.Should().Be(0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SweepOnce_ResumedRun_StillDrainsTheQueue()
    {
        var root = NewRoot();
        try
        {
            var hash = HexHash("d1");
            var path = $"blob/sha256/d1/00/{hash}";
            var full = WriteBlob(root, path, "queued"u8.ToArray());
            DbContext.Add(
                new PendingBlobDeletion
                {
                    ContentHash = "sha256:" + hash,
                    RelativePath = path,
                    QueuedAt = DateTime.UtcNow.AddDays(-3),
                }
            );
            await DbContext.SaveChangesAsync();
            StateStore(root)
                .Save(
                    new BlobSweepState
                    {
                        Run = new BlobSweepRun
                        {
                            StartedAt = DateTime.UtcNow.AddHours(-30),
                            Slot = 1,
                            Phase = BlobSweepPhase.Reconcile,
                            Position = "blob/sha256/f1",
                        },
                    }
                );
            await using var provider = CreateServices();

            await CreateWorker(provider, root).SweepOnce(CancellationToken.None);

            System.IO.File.Exists(full).Should().BeFalse();
            System.IO.File.Exists(TrashFolder(root, path)).Should().BeTrue();
            await using var verify = Fixture.CreateDbContext();
            (await verify.Set<PendingBlobDeletion>().CountAsync()).Should().Be(0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Folders share one reference query, so none becomes the position before its batch is done.
    [Fact]
    public async Task SweepOnce_InterruptedMidReconciliation_RedoesTheUnsettledBatch()
    {
        var root = NewRoot();
        try
        {
            // Shards 0x00 and 0x07 are both slot 0, the one after slot 6.
            var orphans = new[] { "00", "07" }
                .Select(shard =>
                {
                    var orphan = WriteBlob(
                        root,
                        $"blob/sha256/{shard}/00/{HexHash(shard)}",
                        "orphan"u8.ToArray()
                    );
                    System.IO.File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddDays(-3));
                    return orphan;
                })
                .ToList();
            StateStore(root)
                .Save(
                    new BlobSweepState
                    {
                        LastCompletedAt = DateTime.UtcNow.AddDays(-2),
                        LastSlot = 6,
                    }
                );

            using var shutdown = new CancellationTokenSource();
            await using (var provider = CreateServices(new CountingReferenceChecker(shutdown, 1)))
            {
                var sweep = () => CreateWorker(provider, root).SweepOnce(shutdown.Token);
                await sweep.Should().ThrowAsync<OperationCanceledException>();
            }

            orphans.Should().OnlyContain(orphan => System.IO.File.Exists(orphan));
            var saved = StateStore(root).Load().Run;
            saved.Phase.Should().Be(BlobSweepPhase.Reconcile);
            saved.Position.Should().BeNull();

            await using (var provider = CreateServices(new CountingReferenceChecker()))
            {
                await CreateWorker(provider, root).SweepOnce(CancellationToken.None);
            }

            orphans.Should().OnlyContain(orphan => !System.IO.File.Exists(orphan));
            StateStore(root).Load().Run.Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
