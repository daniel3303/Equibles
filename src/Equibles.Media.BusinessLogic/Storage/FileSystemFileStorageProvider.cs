using Equibles.Core.AutoWiring;
using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Media.BusinessLogic.Storage;

/// <summary>
/// Stores bytes on a content-addressed, sharded filesystem tree rooted at the configured
/// path: <c>&lt;root&gt;/&lt;tier&gt;/sha256/&lt;hash[0:2]&gt;/&lt;hash[2:4]&gt;/&lt;hash&gt;</c>. The path is a
/// pure function of the content (SHA-256), giving byte-level deduplication. Writes are
/// crash-safe via <see cref="DurableFileWriter"/> (temp → fsync → atomic rename → fsync dir),
/// and the File row is persisted by the caller afterwards (blob-before-row).
/// </summary>
[Service(ServiceLifetime.Scoped)]
public class FileSystemFileStorageProvider : IFileStorageProvider
{
    private readonly FileStorageOptions _options;

    public FileSystemFileStorageProvider(IOptions<FileStorageOptions> options)
    {
        _options = options.Value;
    }

    public StorageProvider Provider => StorageProvider.FileSystem;

    public async Task Save(File file, byte[] content, string tier)
    {
        var hashHex = ContentAddressedPath.ComputeSha256Hex(content);
        var relativePath = ContentAddressedPath.Build(tier, hashHex);
        var fullPath = Path.Combine(RequireRoot(), ContentAddressedPath.ToOsPath(relativePath));

        await DurableFileWriter.WriteIfMissing(fullPath, content);

        file.Size = content.Length;
        file.StorageProvider = StorageProvider.FileSystem;
        file.RelativePath = relativePath;
        file.ContentHash = ContentAddressedPath.HashPrefix + hashHex;
        file.FileContent = null;
    }

    public Task<byte[]> GetContent(File file)
    {
        return System.IO.File.ReadAllBytesAsync(ResolvePath(file));
    }

    public Task<Stream> OpenRead(File file)
    {
        Stream stream = new FileStream(
            ResolvePath(file),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1 << 16,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        return Task.FromResult(stream);
    }

    private string ResolvePath(File file)
    {
        if (string.IsNullOrEmpty(file.RelativePath))
        {
            throw new InvalidOperationException(
                $"File {file.Id} is FileSystem-stored but has no RelativePath."
            );
        }

        return Path.Combine(RequireRoot(), ContentAddressedPath.ToOsPath(file.RelativePath));
    }

    private string RequireRoot()
    {
        if (string.IsNullOrEmpty(_options.RootPath))
        {
            throw new InvalidOperationException(
                "FileStorage:RootPath is not configured; cannot use the filesystem storage provider."
            );
        }

        return _options.RootPath;
    }
}
