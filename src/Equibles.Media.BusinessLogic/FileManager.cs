using Equibles.Core.AutoWiring;
using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.BusinessLogic.Storage;
using Equibles.Media.Data.Models;
using Equibles.Media.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MimeTypes;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Media.BusinessLogic;

[Service(ServiceLifetime.Scoped, typeof(IFileManager))]
public class FileManager : IFileManager
{
    public static readonly IList<string> AcceptedExtensions =
    [
        "pdf",
        "png",
        "jpg",
        "jpeg",
        "xls",
        "xlsx",
        "doc",
        "docx",
        "txt",
        "psd",
    ];

    public static string AcceptedExtensionsString()
    {
        return string.Concat(".", string.Join(",.", AcceptedExtensions));
    }

    private readonly FileRepository _fileRepository;
    private readonly PendingBlobDeletionRepository _pendingBlobDeletionRepository;
    private readonly DatabaseFileStorageProvider _databaseProvider;
    private readonly FileSystemFileStorageProvider _fileSystemProvider;
    private readonly FileStorageOptions _options;

    public FileManager(
        FileRepository fileRepository,
        PendingBlobDeletionRepository pendingBlobDeletionRepository,
        DatabaseFileStorageProvider databaseProvider,
        FileSystemFileStorageProvider fileSystemProvider,
        IOptions<FileStorageOptions> options
    )
    {
        _fileRepository = fileRepository;
        _pendingBlobDeletionRepository = pendingBlobDeletionRepository;
        _databaseProvider = databaseProvider;
        _fileSystemProvider = fileSystemProvider;
        _options = options.Value;
    }

    /**
     * <summary>
     * Saves a file to the database. The db context is not saved.
     * The file name is used to infer the file extension.
     * </summary>
     * <param name="content">The file content</param>
     * <param name="fileName">The file name</param>
     * <param name="protect">If the file should be protected using a security token for access</param>
     */
    public async Task<File> SaveFile(byte[] content, string fileName, bool protect = false)
    {
        var fileExtension = Path.GetExtension(fileName)?.TrimStart('.');
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

        if (string.IsNullOrEmpty(fileExtension))
        {
            throw new ArgumentException("The file extension cannot be null or empty.");
        }

        // Enforce the curated allowlist (whitelist, case-insensitive). Without
        // this, an unenforced allowlist gives false assurance — an .exe/.svg/
        // .html payload would be persisted and later served.
        if (!AcceptedExtensions.Contains(fileExtension, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The file extension '.{fileExtension}' is not allowed. Accepted extensions: {AcceptedExtensionsString()}."
            );
        }

        var contentType = MimeTypeMap.GetMimeType(fileExtension);
        if (string.IsNullOrEmpty(contentType))
        {
            contentType = "application/octet-stream";
        }

        var file = new File()
        {
            Extension = fileExtension,
            Name = fileNameWithoutExtension,
            ContentType = contentType,
        };

        // User uploads always stay in the database (small, hot, served directly).
        await _databaseProvider.Save(file, content, FileStorageTiers.Blob);

        _fileRepository.Add(file);
        return file;
    }

    /// <summary>
    /// Persists a trusted, system-generated blob with an explicit extension and content
    /// type, bypassing the <see cref="AcceptedExtensions"/> upload allowlist. The db context
    /// is not saved. Intended only for content the application produces itself (e.g. a
    /// gzip-compressed XBRL envelope captured during SEC ingest), never for user uploads —
    /// those must go through <see cref="SaveFile"/> so the allowlist is enforced.
    /// </summary>
    public async Task<File> SaveInternalFile(
        byte[] content,
        string name,
        string extension,
        string contentType,
        StorageProvider storage = null,
        string tier = null
    )
    {
        var file = new File()
        {
            Extension = extension,
            Name = name,
            ContentType = contentType,
        };

        var provider = ResolveWriteProvider(storage);
        await provider.Save(file, content, tier ?? FileStorageTiers.Blob);

        _fileRepository.Add(file);
        return file;
    }

    public Task<byte[]> GetContent(File file)
    {
        return ResolveProvider(file.StorageProvider).GetContent(file);
    }

    public Task<Stream> OpenRead(File file)
    {
        return ResolveProvider(file.StorageProvider).OpenRead(file);
    }

    /// <summary>
    /// Deletes a file from the database. The db context is not saved. Filesystem-stored
    /// bytes are not deleted inline — content addressing means another row may reference
    /// the same path, and an inline unlink can race an identical re-upload that dedup-skips
    /// onto the existing blob. Instead the blob is queued and the deletion sweep retires it
    /// after re-checking references, in the same SaveChanges as the row delete so the mark
    /// can never outlive a rolled-back delete.
    /// </summary>
    /// <param name="file">The file to delete</param>
    public void DeleteFile(File file)
    {
        if (file == null)
            return;

        if (
            file.StorageProvider == StorageProvider.FileSystem
            && file.ContentHash != null
            && file.RelativePath != null
        )
        {
            _pendingBlobDeletionRepository.Add(
                new PendingBlobDeletion
                {
                    ContentHash = file.ContentHash,
                    RelativePath = file.RelativePath,
                }
            );
        }

        _fileRepository.Delete(file);
    }

    // Filesystem writes are opt-in: when the store is disabled, fall back to the database
    // so a deployment without a configured root keeps working unchanged.
    private IFileStorageProvider ResolveWriteProvider(StorageProvider requested)
    {
        var target = requested ?? StorageProvider.Database;
        if (target == StorageProvider.FileSystem && !_options.Enabled)
        {
            target = StorageProvider.Database;
        }

        return ResolveProvider(target);
    }

    private IFileStorageProvider ResolveProvider(StorageProvider provider)
    {
        if (provider == StorageProvider.FileSystem)
        {
            return _fileSystemProvider;
        }

        return _databaseProvider;
    }
}
