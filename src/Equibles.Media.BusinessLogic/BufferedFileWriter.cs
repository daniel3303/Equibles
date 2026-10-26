using Equibles.Core.AutoWiring;
using Equibles.Media.BusinessLogic.Storage;
using Equibles.Media.Repositories;
using Microsoft.Extensions.DependencyInjection;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Media.BusinessLogic;

/// <summary>
/// The buffered counterpart of <see cref="FileManager.SaveInternalFile"/>: the same scoped router and
/// repository, so a batch staged here is the one <see cref="SyncStore"/> flushes within the scope.
/// </summary>
[Service(ServiceLifetime.Scoped, typeof(IBufferedFileWriter))]
public class BufferedFileWriter : IBufferedFileWriter
{
    private readonly FileRepository _fileRepository;
    private readonly FileStorageRouter _storageRouter;

    public BufferedFileWriter(FileRepository fileRepository, FileStorageRouter storageRouter)
    {
        _fileRepository = fileRepository;
        _storageRouter = storageRouter;
    }

    public async Task<File> SaveInternalFileBuffered(
        byte[] content,
        string name,
        string extension,
        string contentType,
        string tier = null
    )
    {
        var file = new File
        {
            Extension = extension,
            Name = name,
            ContentType = contentType,
        };
        await _storageRouter.SaveBuffered(file, content, tier);
        _fileRepository.Add(file);
        return file;
    }

    public void SyncStore() => _storageRouter.SyncStore();
}
