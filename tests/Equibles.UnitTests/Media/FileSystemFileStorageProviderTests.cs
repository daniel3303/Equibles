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
}
