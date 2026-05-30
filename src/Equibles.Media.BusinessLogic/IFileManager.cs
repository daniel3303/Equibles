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
    /// </summary>
    public Task<File> SaveInternalFile(
        byte[] content,
        string name,
        string extension,
        string contentType
    );

    public void DeleteFile(File file);
}
