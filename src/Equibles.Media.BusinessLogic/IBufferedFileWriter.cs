using File = Equibles.Media.Data.Models.File;

namespace Equibles.Media.BusinessLogic;

/// <summary>
/// Stores a batch of system-generated blobs without a per-file fsync and makes the whole batch
/// durable with one <see cref="SyncStore"/> call, which the caller MUST issue before committing
/// the rows that reference the blobs. Kept apart from <see cref="IFileManager"/> so its many
/// fakes stay untouched and a substitute of it never hides a missed sync.
/// </summary>
public interface IBufferedFileWriter
{
    /// <summary>
    /// <see cref="IFileManager.SaveInternalFile"/> without the per-file fsync; the database
    /// backend, which has no buffered mode, saves as usual.
    /// </summary>
    public Task<File> SaveInternalFileBuffered(
        byte[] content,
        string name,
        string extension,
        string contentType,
        string tier = null
    );

    /// <summary>Makes every buffered save durable and visible; one call covers a whole batch.</summary>
    public void SyncStore();
}
