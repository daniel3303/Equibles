using System.Buffers.Binary;
using System.IO.Compression;
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
        var stored = Encode(file, content);
        var fullPath = StampAndResolve(file, stored, tier);
        await DurableFileWriter.WriteIfMissing(fullPath, stored);
    }

    /// <summary>
    /// Bulk-migration variant of <see cref="Save"/>: writes buffered (no per-file fsync).
    /// The caller MUST call <see cref="SyncStore"/> after the batch, before committing the
    /// database rows, so bytes are durable before any row points at them.
    /// </summary>
    public async Task SaveBuffered(File file, byte[] content, string tier)
    {
        var stored = Encode(file, content);
        var stamp = await WriteBuffered(stored, tier);
        file.RelativePath = stamp.RelativePath;
        file.ContentHash = stamp.ContentHash;
        file.FileContent = null;
    }

    /// <summary>
    /// Entity-free buffered write for bulk migration: stores the bytes at their
    /// content-addressed path (no fsync — pair with <see cref="SyncStore"/>) and returns the
    /// values to stamp on the row later. Lets parallel workers write blobs without sharing a
    /// DbContext; the caller applies the row changes after the durability barrier.
    /// </summary>
    public async Task<(string RelativePath, string ContentHash)> WriteBuffered(
        byte[] content,
        string tier
    )
    {
        var hashHex = ContentAddressedPath.ComputeSha256Hex(content);
        var relativePath = ContentAddressedPath.Build(tier, hashHex);
        var fullPath = Path.Combine(RequireRoot(), ContentAddressedPath.ToOsPath(relativePath));
        await DurableFileWriter.WriteIfMissingBuffered(fullPath, content);
        return (relativePath, ContentAddressedPath.HashPrefix + hashHex);
    }

    /// <summary>Flushes the whole store's filesystem to stable storage (batch durability barrier).</summary>
    public void SyncStore()
    {
        DurableFileWriter.SyncFileSystem(RequireRoot());
    }

    private string StampAndResolve(File file, byte[] content, string tier)
    {
        var hashHex = ContentAddressedPath.ComputeSha256Hex(content);
        var relativePath = ContentAddressedPath.Build(tier, hashHex);
        var fullPath = Path.Combine(RequireRoot(), ContentAddressedPath.ToOsPath(relativePath));

        file.RelativePath = relativePath;
        file.ContentHash = ContentAddressedPath.HashPrefix + hashHex;
        file.FileContent = null;
        return fullPath;
    }

    private byte[] Encode(File file, byte[] content)
    {
        file.Size = content.LongLength;
        file.StorageProvider = StorageProvider.FileSystem;
        if (
            !_options.CompressTextFiles
            || !string.Equals(
                file.ContentType?.Split(';', 2)[0].Trim(),
                "text/plain",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return content;
        }

        file.StorageProvider = StorageProvider.FileSystemGzip;
        if (content.Length == 0)
        {
            // GZipStream emits no member when no bytes were written. Store a valid empty
            // gzip member so every FileSystemGzip blob has the same interoperable format.
            return Convert.FromHexString("1F8B080000000000000303000000000000000000");
        }

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(content);
        }

        return output.ToArray();
    }

    public async Task<byte[]> GetContent(File file)
    {
        var stored = await System.IO.File.ReadAllBytesAsync(ResolvePath(file));
        if (file.StorageProvider != StorageProvider.FileSystemGzip)
        {
            return stored;
        }

        // Validate the stored hash before decoding: GZipStream can accept a missing footer.
        // Size stays logical, while the path and hash always describe the physical bytes.
        if (
            stored.Length < 18
            || stored[0] != 0x1f
            || stored[1] != 0x8b
            || stored[2] != 8
            || file.ContentHash
                != ContentAddressedPath.HashPrefix + ContentAddressedPath.ComputeSha256Hex(stored)
            || file.Size < 0
            || file.Size > Array.MaxLength
            || BinaryPrimitives.ReadUInt32LittleEndian(stored.AsSpan(stored.Length - 4))
                != (uint)file.Size
        )
        {
            throw new InvalidDataException(
                $"Compressed file {file.Id} has invalid storage metadata or bytes."
            );
        }

        using var input = new MemoryStream(stored, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        var content = new byte[(int)file.Size];
        await gzip.ReadExactlyAsync(content);
        if (gzip.ReadByte() != -1)
        {
            throw new InvalidDataException($"Compressed file {file.Id} exceeds its original size.");
        }

        return content;
    }

    public async Task<Stream> OpenRead(File file)
    {
        if (file.StorageProvider == StorageProvider.FileSystemGzip)
        {
            // Preserve seek/length/range semantics for callers; large audio and PDF blobs
            // retain their streaming path and are never selected for text compression.
            return new MemoryStream(await GetContent(file), writable: false);
        }

        return new FileStream(
            ResolvePath(file),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1 << 16,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
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
