using Equibles.Media.Data.Models;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Media.BusinessLogic;

public interface IFileManager
{
    public Task<File> SaveFile(byte[] content, string fileName, bool protect = false);

    /// <summary>
    /// Persists a trusted, system-generated blob (never a user upload) with an explicit
    /// extension and content type, bypassing the <see cref="FileManager.AcceptedExtensions"/>
    /// upload allowlist. Use only for content the application itself produces — e.g. a
    /// gzip-compressed XBRL envelope captured during SEC ingest — never for inbound files.
    /// <paramref name="storage"/> selects the backend (default Database preserves the original
    /// behavior; SEC ingest passes FileSystem). A FileSystem request falls back to Database when
    /// the store is disabled. <paramref name="tier"/> selects the filesystem durability partition
    /// (see <see cref="Storage.FileStorageTiers"/>).
    /// </summary>
    public Task<File> SaveInternalFile(
        byte[] content,
        string name,
        string extension,
        string contentType,
        StorageProvider storage = null,
        string tier = null
    );

    /// <summary>Reads a file's full bytes, dispatching on where it is stored.</summary>
    public Task<byte[]> GetContent(File file);

    /// <summary>Opens a read stream over a file's bytes, dispatching on where it is stored. The caller disposes it.</summary>
    public Task<Stream> OpenRead(File file);

    public void DeleteFile(File file);
}
