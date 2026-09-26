using System.IO.Compression;
using System.Text;
using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.BusinessLogic.Storage;
using Equibles.Media.Data.Models;
using Microsoft.Extensions.Options;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.UnitTests.Media;

public sealed class CompressedFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "eq-gzip-" + Guid.NewGuid().ToString("N")
    );

    private FileSystemFileStorageProvider Provider(bool enabled = true) =>
        new(
            Options.Create(
                new FileStorageOptions
                {
                    Enabled = true,
                    RootPath = _root,
                    CompressTextFiles = enabled,
                }
            )
        );

    [Theory]
    [InlineData("text/plain", false)]
    [InlineData(" TEXT/PLAIN ; charset=utf-8", true)]
    public async Task Save_CompressesPhysicalBytes_PreservesLogicalMetadataAndSeekableReads(
        string mime,
        bool buffered
    )
    {
        var content = Encoding.UTF8.GetBytes(
            string.Concat(Enumerable.Repeat("Olá 世界 — filing text\n", 1000))
        );
        var file = new File
        {
            ContentType = mime,
            Extension = "txt",
            Name = "original",
        };
        var id = file.Id;
        var provider = Provider();
        if (buffered)
        {
            await provider.SaveBuffered(file, content, FileStorageTiers.Blob);
            provider.SyncStore();
        }
        else
        {
            await provider.Save(file, content, FileStorageTiers.Blob);
        }

        file.Id.Should().Be(id);
        file.Size.Should().Be(content.Length);
        file.ContentType.Should().Be(mime);
        file.Extension.Should().Be("txt");
        file.StorageProvider.Should().Be(StorageProvider.FileSystemGzip);
        file.FileContent.Should().BeNull();
        var stored = await System.IO.File.ReadAllBytesAsync(Path.Combine(_root, file.RelativePath));
        stored.Length.Should().BeLessThan(content.Length / 2);
        file.ContentHash.Should().Be("sha256:" + ContentAddressedPath.ComputeSha256Hex(stored));
        file.RelativePath.Should().EndWith(file.ContentHash[7..]);
        using var gzip = new GZipStream(new MemoryStream(stored), CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        await gzip.CopyToAsync(decoded);
        decoded.ToArray().Should().Equal(content);

        // Disabling writes must never disable reads of an already migrated corpus.
        (await Provider(false).GetContent(file))
            .Should()
            .Equal(content);
        await using var stream = await Provider(false).OpenRead(file);
        stream.CanSeek.Should().BeTrue();
        stream.Length.Should().Be(content.Length);
        stream.Seek(7, SeekOrigin.Begin);
        stream.ReadByte().Should().Be(content[7]);
    }

    [Theory]
    [InlineData("application/gzip")]
    [InlineData("application/pdf")]
    [InlineData("audio/mp4")]
    [InlineData("image/jpeg")]
    [InlineData(null)]
    public async Task Save_OtherTypes_KeepExactPhysicalBytes(string mime)
    {
        var content = "already encoded bytes"u8.ToArray();
        var file = new File { ContentType = mime };
        await Provider().Save(file, content, FileStorageTiers.Blob);
        file.StorageProvider.Should().Be(StorageProvider.FileSystem);
        (await System.IO.File.ReadAllBytesAsync(Path.Combine(_root, file.RelativePath)))
            .Should()
            .Equal(content);
        (await Provider().GetContent(file)).Should().Equal(content);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public async Task Save_EmptyAndNonUtf8Text_RoundTripsWithoutChangingBytes(int length)
    {
        var content = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
        var first = new File { ContentType = "text/plain" };
        var second = new File { ContentType = "text/plain" };
        await Provider().Save(first, content, FileStorageTiers.Blob);
        await Provider().Save(second, content, FileStorageTiers.Blob);
        first.StorageProvider.Should().Be(StorageProvider.FileSystemGzip);
        second.RelativePath.Should().Be(first.RelativePath);
        (await Provider().GetContent(first)).Should().Equal(content);
    }

    [Fact]
    public async Task LegacyRawText_RemainsReadableAfterEnablingCompression()
    {
        var file = new File { ContentType = "text/plain" };
        var content = "old text"u8.ToArray();
        await Provider(false).Save(file, content, FileStorageTiers.Blob);
        file.StorageProvider.Should().Be(StorageProvider.FileSystem);
        (await Provider().GetContent(file)).Should().Equal(content);
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("corrupt")]
    [InlineData("short-size")]
    [InlineData("long-size")]
    public async Task Read_DamagedBytesOrWrongSize_FailsInsteadOfReturningPartialText(string damage)
    {
        var file = new File { ContentType = "text/plain" };
        await Provider().Save(file, "preserve every byte"u8.ToArray(), FileStorageTiers.Blob);
        var path = Path.Combine(_root, file.RelativePath);
        var stored = await System.IO.File.ReadAllBytesAsync(path);
        if (damage == "truncated")
            await System.IO.File.WriteAllBytesAsync(path, stored[..^8]);
        else if (damage == "corrupt")
        {
            stored[12] ^= 0xff;
            await System.IO.File.WriteAllBytesAsync(path, stored);
        }
        else
            file.Size += damage == "short-size" ? -1 : 1;
        var read = () => Provider().GetContent(file);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Read_PythonMigrationGzip_IsCompatible()
    {
        var raw = Encoding.UTF8.GetBytes("Python migration — exact bytes");
        var stored = Convert.FromBase64String(
            "H4sIAAAAAAAA/wuoLMnIz1PIzUwvSizJBLIeNUxRSK1ITC5RSKosSS0GAKesgM0gAAAA"
        );
        var hash = ContentAddressedPath.ComputeSha256Hex(stored);
        var file = new File
        {
            StorageProvider = StorageProvider.FileSystemGzip,
            Size = raw.Length,
            ContentHash = "sha256:" + hash,
            RelativePath = ContentAddressedPath.Build(FileStorageTiers.Blob, hash),
        };
        var path = Path.Combine(_root, file.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        await System.IO.File.WriteAllBytesAsync(path, stored);
        (await Provider(false).GetContent(file)).Should().Equal(raw);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
