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
    /// The backend is chosen by <see cref="Storage.FileStorageRouter"/>: the filesystem store
    /// when it is enabled, otherwise the database — a caller cannot request the database while
    /// the store is enabled. <paramref name="tier"/> selects the filesystem durability partition
    /// (see <see cref="Storage.FileStorageTiers"/>).
    /// </summary>
    public Task<File> SaveInternalFile(
        byte[] content,
        string name,
        string extension,
        string contentType,
        string tier = null
    );

    /// <summary>Reads a file's full bytes, dispatching on where it is stored.</summary>
    public Task<byte[]> GetContent(File file);

    /// <summary>Opens a read stream over a file's bytes, dispatching on where it is stored. The caller disposes it.</summary>
    public Task<Stream> OpenRead(File file);

    public void DeleteFile(File file);
}
