using Equibles.Media.Data.Models;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Media.BusinessLogic.Storage;

/// <summary>
/// A backend that physically stores and retrieves a <see cref="File"/>'s bytes. One
/// implementation per <see cref="StorageProvider"/> value; <c>FileManager</c> dispatches
/// on <c>File.StorageProvider</c>.
/// </summary>
public interface IFileStorageProvider
{
    /// <summary>The storage backend this implementation handles.</summary>
    StorageProvider Provider { get; }

    /// <summary>
    /// Persists content for a newly-created File, stamping the File's storage metadata
    /// (Size, StorageProvider, and for the filesystem backend RelativePath + ContentHash).
    /// Does not save the DbContext — the caller does. <paramref name="tier"/> selects the
    /// durability partition (see <see cref="FileStorageTiers"/>); the database backend ignores it.
    /// </summary>
    Task Save(File file, byte[] content, string tier);

    /// <summary>Reads the full content of a stored File.</summary>
    Task<byte[]> GetContent(File file);

    /// <summary>Opens a read stream over a stored File's content. The caller disposes it.</summary>
    Task<Stream> OpenRead(File file);
}
