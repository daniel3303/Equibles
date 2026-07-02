using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.BusinessLogic.Storage;
using Equibles.Media.Data.Models;
using Equibles.Media.HostedService;
using Equibles.Media.HostedService.Configuration;
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
/// references even though they were never queued (the cascade-delete case).
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
    }

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
}
