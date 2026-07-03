using Equibles.Core.AutoWiring;
using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Media.BusinessLogic.Storage;

/// <summary>
/// The single decision point for where blob bytes live. When the filesystem store is enabled,
/// every new write goes to the content-addressed filesystem — there is deliberately no code
/// path that persists new bytes in the database. When it is disabled (a self-hosted deployment
/// without a configured root), writes fall back to the database so the application keeps
/// working unchanged. Reads always dispatch on the provider recorded on the row, so blobs
/// written under either mode stay readable after the setting changes.
/// </summary>
[Service(ServiceLifetime.Scoped)]
public class FileStorageRouter
{
    private readonly DatabaseFileStorageProvider _databaseProvider;
    private readonly FileSystemFileStorageProvider _fileSystemProvider;
    private readonly FileStorageOptions _options;

    public FileStorageRouter(
        DatabaseFileStorageProvider databaseProvider,
        FileSystemFileStorageProvider fileSystemProvider,
        IOptions<FileStorageOptions> options
    )
    {
        _databaseProvider = databaseProvider;
        _fileSystemProvider = fileSystemProvider;
        _options = options.Value;
    }

    /// <summary>
    /// The provider new bytes are written through: the filesystem whenever the store is enabled,
    /// otherwise the database. There is no way for a caller to request the database while the
    /// store is enabled — that is the point.
    /// </summary>
    public IFileStorageProvider WriteProvider =>
        _options.Enabled ? _fileSystemProvider : _databaseProvider;

    /// <summary>Writes bytes for a new file through the write provider, stamping the row.</summary>
    public Task Save(File file, byte[] content, string tier = null) =>
        WriteProvider.Save(file, content, tier ?? FileStorageTiers.Blob);

    /// <summary>Resolves the provider for reading a file by the value recorded when it was written.</summary>
    public IFileStorageProvider ReadProvider(StorageProvider stored) =>
        stored == StorageProvider.FileSystem ? _fileSystemProvider : _databaseProvider;
}
