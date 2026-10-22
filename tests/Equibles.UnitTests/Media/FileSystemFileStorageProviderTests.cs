using System.Text;
using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.BusinessLogic.Storage;
using Equibles.Media.Data.Models;
using Microsoft.Extensions.Options;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.UnitTests.Media;

public class FileSystemFileStorageProviderTests : IDisposable
{
    private readonly string _root;
    private readonly FileSystemFileStorageProvider _provider;

    public FileSystemFileStorageProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "eq-blobstore-" + Guid.NewGuid().ToString("N"));
        _provider = new FileSystemFileStorageProvider(
            Options.Create(new FileStorageOptions { Enabled = true, RootPath = _root })
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // A filesystem write must land the bytes at the content-addressed path and stamp the
    // File with FileSystem + RelativePath + ContentHash (and clear the DB FileContent).
    [Fact]
    public async Task Save_WritesContentAddressedFile_AndStampsMetadata()
    {
        var file = new File();
        var content = Encoding.UTF8.GetBytes("filesystem payload");

        await _provider.Save(file, content, FileStorageTiers.Blob);

        file.StorageProvider.Should().Be(StorageProvider.FileSystem);
        file.Size.Should().Be(content.Length);
        file.RelativePath.Should().StartWith("blob/sha256/");
        file.ContentHash.Should().StartWith("sha256:");
        file.FileContent.Should().BeNull();

        var onDisk = Path.Combine(_root, ContentAddressedPath.ToOsPath(file.RelativePath));
        System.IO.File.Exists(onDisk).Should().BeTrue();
        (await System.IO.File.ReadAllBytesAsync(onDisk)).Should().Equal(content);
    }

    // A crash between a buffered write's rename and its barrier can leave a zero-length or
    // zero-filled file at the content path; the next write of those bytes replaces it instead
    // of adopting it as a dedup hit.
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Save_DamagedFileAtTheContentPath_IsReplaced(bool buffered, bool zeroFilled)
    {
        var content = Encoding.UTF8.GetBytes("original announcement bytes");
        var relativePath = ContentAddressedPath.Build(
            FileStorageTiers.Blob,
            ContentAddressedPath.ComputeSha256Hex(content)
        );
        var onDisk = Path.Combine(_root, ContentAddressedPath.ToOsPath(relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(onDisk));
        await System.IO.File.WriteAllBytesAsync(onDisk, zeroFilled ? new byte[content.Length] : []);
        var file = new File();

        if (buffered)
        {
            await _provider.SaveBuffered(file, content, FileStorageTiers.Blob);
            _provider.SyncStore();
        }
        else
        {
            await _provider.Save(file, content, FileStorageTiers.Blob);
        }

        file.RelativePath.Should().Be(relativePath);
        (await System.IO.File.ReadAllBytesAsync(onDisk)).Should().Equal(content);
    }

    // The barrier throws when the store cannot be flushed, so a caller never commits rows over
    // bytes the disk did not take.
    [Fact]
    public void SyncStore_UnflushableRoot_Throws()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var provider = new FileSystemFileStorageProvider(
            Options.Create(
                new FileStorageOptions { Enabled = true, RootPath = Path.Combine(_root, "missing") }
            )
        );

        var sync = () => provider.SyncStore();

        sync.Should().Throw<IOException>();
    }

    // A failed flush deletes the files its batch wrote, so no later write keeps bytes that may
    // exist only in the page cache, and keeps a file an earlier durable write stored.
    [Fact]
    public async Task SyncStore_FailedFlush_DeletesOnlyTheBatchsFiles()
    {
        var kept = Encoding.UTF8.GetBytes("already durable bytes");
        var written = Encoding.UTF8.GetBytes("unflushed batch bytes");
        var provider = new FailingFlushProvider(_root);
        var keptFile = new File();
        var writtenFile = new File();
        await provider.Save(keptFile, kept, FileStorageTiers.Blob);
        await provider.SaveBuffered(new File(), kept, FileStorageTiers.Blob);
        await provider.SaveBuffered(writtenFile, written, FileStorageTiers.Blob);

        var sync = () => provider.SyncStore();

        sync.Should().Throw<IOException>();
        (await System.IO.File.ReadAllBytesAsync(PathOf(keptFile))).Should().Equal(kept);
        System.IO.File.Exists(PathOf(writtenFile)).Should().BeFalse();
        await _provider.Save(new File(), written, FileStorageTiers.Blob);
        (await System.IO.File.ReadAllBytesAsync(PathOf(writtenFile))).Should().Equal(written);
    }

    // A batch abandoned before its barrier leaves none of its files behind.
    [Fact]
    public async Task Dispose_BeforeTheBarrier_DeletesTheBatchsFiles()
    {
        var file = new File();
        var provider = new FileSystemFileStorageProvider(
            Options.Create(new FileStorageOptions { Enabled = true, RootPath = _root })
        );
        await provider.SaveBuffered(
            file,
            Encoding.UTF8.GetBytes("abandoned"),
            FileStorageTiers.Blob
        );

        provider.Dispose();

        System.IO.File.Exists(PathOf(file)).Should().BeFalse();
    }

    // Content addressing gives free dedup: identical bytes map to one path and the second
    // write is a no-op, never an error, even though the target already exists.
    [Fact]
    public async Task Save_IdenticalContentTwice_DeduplicatesToSamePath()
    {
        var content = Encoding.UTF8.GetBytes("shared exhibit bytes");
        var first = new File();
        var second = new File();

        await _provider.Save(first, content, FileStorageTiers.Blob);
        await _provider.Save(second, content, FileStorageTiers.Blob);

        second.RelativePath.Should().Be(first.RelativePath);
        var shardDir = Path.GetDirectoryName(
            Path.Combine(_root, ContentAddressedPath.ToOsPath(first.RelativePath))
        );
        Directory.GetFiles(shardDir).Should().HaveCount(1);
    }

    [Fact]
    public async Task GetContentAndOpenRead_RoundTripStoredBytes()
    {
        var file = new File();
        var content = Encoding.UTF8.GetBytes("stream me back");
        await _provider.Save(file, content, FileStorageTiers.Blob);

        (await _provider.GetContent(file)).Should().Equal(content);

        await using var stream = await _provider.OpenRead(file);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(content);
    }

    // Guard: a FileSystem file with no path is a corrupt pointer and must fail loudly,
    // never silently return empty content.
    [Fact]
    public async Task GetContent_FileSystemFileWithoutRelativePath_Throws()
    {
        var file = new File { StorageProvider = StorageProvider.FileSystem };

        var act = () => _provider.GetContent(file);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private string PathOf(File file) =>
        Path.Combine(_root, ContentAddressedPath.ToOsPath(file.RelativePath));

    private sealed class FailingFlushProvider(string root)
        : FileSystemFileStorageProvider(
            Options.Create(new FileStorageOptions { Enabled = true, RootPath = root })
        )
    {
        protected override void Flush(FileSystemSyncHandle barrier)
        {
            throw new IOException("Simulated writeback failure.");
        }
    }
}
