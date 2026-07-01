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
using FileContent = Equibles.Media.Data.Models.FileContent;

namespace Equibles.IntegrationTests.Media;

/// <summary>
/// Exercises the backfill drain against real Postgres — the point the in-memory unit test
/// can't reach. The eligibility query compares a value-converted smart-enum
/// (<c>StorageProvider == StorageProvider.Database</c>); an untranslatable variant was inert
/// at runtime and only a real relational provider surfaces that, so this pins both the query
/// translation and the full move (row flipped, FileContent deleted, bytes on disk), while
/// confirming images and byte-less rows are left alone.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FileBackfillWorkerDrainTests : ParadeDbMcpTestBase
{
    public FileBackfillWorkerDrainTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task DrainOnce_MovesDatabaseBlobsToDisk_LeavesImagesAndBytelessRows()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "eq-backfill-it-" + Guid.NewGuid().ToString("N")
        );

        var eligibleBytes = "gzip-xbrl-envelope"u8.ToArray();
        var audioBytes = "webcast-m4a-recording"u8.ToArray();
        var imageBytes = "headshot-jpeg"u8.ToArray();

        var eligible = new File
        {
            Name = "xbrl-envelope",
            Extension = "gz",
            ContentType = "application/gzip",
            StorageProvider = StorageProvider.Database,
        };
        eligible.FileContent = new FileContent { File = eligible, Bytes = eligibleBytes };

        var audio = new File
        {
            Name = "webcast-recording",
            Extension = "m4a",
            ContentType = "audio/mp4",
            StorageProvider = StorageProvider.Database,
        };
        audio.FileContent = new FileContent { File = audio, Bytes = audioBytes };

        var image = new Image
        {
            Name = "headshot",
            Extension = "jpg",
            ContentType = "image/jpeg",
            Width = 1,
            Height = 1,
            StorageProvider = StorageProvider.Database,
        };
        image.FileContent = new FileContent { File = image, Bytes = imageBytes };

        var byteless = new File
        {
            Name = "indexed-not-downloaded",
            Extension = "txt",
            ContentType = "text/plain",
            StorageProvider = StorageProvider.Database,
        };
        byteless.FileContent = new FileContent { File = byteless, Bytes = null };

        DbContext.AddRange(eligible, audio, image, byteless);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        try
        {
            var storageOptions = Options.Create(
                new FileStorageOptions { Enabled = true, RootPath = root }
            );
            var services = new ServiceCollection();
            services.AddScoped<EquiblesFinancialDbContext>(_ => Fixture.CreateDbContext());
            services.AddScoped(_ => new FileSystemFileStorageProvider(storageOptions));
            await using var provider = services.BuildServiceProvider();

            var worker = new FileBackfillWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                storageOptions,
                Options.Create(new FileBackfillOptions { Enabled = true, BatchSize = 100 }),
                NullLogger<FileBackfillWorker>()
            );

            var result = await worker.DrainOnce(CancellationToken.None);

            result.Claimed.Should().Be(2);
            result.Moved.Should().Be(2);

            await using var verify = Fixture.CreateDbContext();

            var movedFile = await verify
                .Set<File>()
                .Include(f => f.FileContent)
                .FirstAsync(f => f.Id == eligible.Id);
            movedFile.StorageProvider.Should().Be(StorageProvider.FileSystem);
            movedFile.RelativePath.Should().StartWith("blob/sha256/");
            movedFile.ContentHash.Should().StartWith("sha256:");
            movedFile.FileContent.Should().BeNull();

            var onDisk = Path.Combine(
                root,
                movedFile.RelativePath.Replace('/', Path.DirectorySeparatorChar)
            );
            System.IO.File.Exists(onDisk).Should().BeTrue();
            (await System.IO.File.ReadAllBytesAsync(onDisk)).Should().Equal(eligibleBytes);

            // Audio routes to its own durability tier so a future mirrored mount at
            // <root>/audio covers the hard-to-recapture recordings.
            var audioAfter = await verify
                .Set<File>()
                .Include(f => f.FileContent)
                .FirstAsync(f => f.Id == audio.Id);
            audioAfter.StorageProvider.Should().Be(StorageProvider.FileSystem);
            audioAfter.RelativePath.Should().StartWith("audio/sha256/");
            audioAfter.FileContent.Should().BeNull();
            var audioOnDisk = Path.Combine(
                root,
                audioAfter.RelativePath.Replace('/', Path.DirectorySeparatorChar)
            );
            (await System.IO.File.ReadAllBytesAsync(audioOnDisk)).Should().Equal(audioBytes);

            var imageAfter = await verify
                .Set<File>()
                .Include(f => f.FileContent)
                .FirstAsync(f => f.Id == image.Id);
            imageAfter.StorageProvider.Should().Be(StorageProvider.Database);
            imageAfter.FileContent.Bytes.Should().Equal(imageBytes);

            var bytelessAfter = await verify.Set<File>().FirstAsync(f => f.Id == byteless.Id);
            bytelessAfter.StorageProvider.Should().Be(StorageProvider.Database);
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
